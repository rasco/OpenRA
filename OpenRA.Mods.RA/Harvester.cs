#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OpenRA.Mods.RA.Activities;
using OpenRA.Mods.RA.Move;
using OpenRA.Mods.RA.Orders;
using OpenRA.Traits;

namespace OpenRA.Mods.RA
{
	public class HarvesterInfo : ITraitInfo
	{
		public readonly int Capacity = 28;
		public readonly int UnloadTicksPerBale = 4;
		public readonly int PipCount = 7;
		public readonly string[] Resources = { };
		public readonly decimal FullyLoadedSpeed = .85m;

		public object Create(ActorInitializer init) { return new Harvester(init.self, this); }
	}

	public class Harvester : IIssueOrder, IResolveOrder, IPips, 
		IRenderModifier, IExplodeModifier, IOrderVoice,
		ISpeedModifier, ISync
	{
		Dictionary<ResourceTypeInfo, int> contents = new Dictionary<ResourceTypeInfo, int>();
		
		[Sync]
		public bool Visible = true;
		
		[Sync]
		public Actor LinkedProc = null;
		
		[Sync]
		public int2 LastHarvestedCell = int2.Zero;
				
		[Sync]
		public int ContentValue { get { return contents.Sum(c => c.Key.ValuePerUnit*c.Value); } }
		
		[Sync]
		int currentUnloadTicks;
		
		readonly HarvesterInfo Info;
		public Harvester(Actor self, HarvesterInfo info)
		{
			Info = info;
			self.QueueActivity( new CallFunc( () => ChooseNewProc(self, null)));
		}
		
		public void ChooseNewProc(Actor self, Actor ignore)
		{
			LinkedProc = ClosestProc(self, ignore);
		}
		
		Actor ClosestProc(Actor self, Actor ignore)
		{
            var refs = self.World.ActorsWithTrait<IAcceptOre>()
                .Where(x => x.Actor != ignore && x.Actor.Owner == self.Owner)
                .ToList();
			var mi = self.Info.Traits.Get<MobileInfo>();
			var path = self.World.WorldActor.Trait<PathFinder>().FindPath(
				PathSearch.FromPoints(self.World, mi,
                    refs.Select(r => r.Actor.Location + r.Trait.DeliverOffset),
					self.Location, false));
			path.Reverse();
			if (path.Count != 0)
				return refs.Where(x => x.Actor.Location + x.Trait.DeliverOffset == path[0])
                    .Select(a => a.Actor).FirstOrDefault();
			else
				return null;
		}

		public bool IsFull { get { return contents.Values.Sum() == Info.Capacity; } }
		public bool IsEmpty { get { return contents.Values.Sum() == 0; } }
		
		public void AcceptResource(ResourceType type)
		{
			if (!contents.ContainsKey(type.info)) contents[type.info] = 1;
			else contents[type.info]++;
		}

		// Returns true when unloading is complete
		public bool TickUnload(Actor self, Actor proc)
		{
			if (!proc.IsInWorld)
				return false;	// fail to deliver if there is no proc.
			
			// Wait until the next bale is ready
			if (--currentUnloadTicks > 0)
				return false;
			
			if (contents.Keys.Count > 0)
			{
				var type = contents.First().Key;
				var iao = proc.Trait<IAcceptOre>();
				if (!iao.CanGiveOre(type.ValuePerUnit))
					return false;
				
				iao.GiveOre(type.ValuePerUnit);
				if (--contents[type] == 0)
					contents.Remove(type);
				
				currentUnloadTicks = Info.UnloadTicksPerBale;
			}
			
			return contents.Count == 0;
		}
		
		public IEnumerable<IOrderTargeter> Orders
		{
			get
			{
				yield return new EnterOrderTargeter<IAcceptOre>( "Deliver", 5, false, true, _ => true, _ => !IsEmpty );
				yield return new HarvestOrderTargeter();
			}
		}

		public Order IssueOrder( Actor self, IOrderTargeter order, Target target, bool queued )
		{
			if( order.OrderID == "Deliver" )
				return new Order(order.OrderID, self, queued) { TargetActor = target.Actor };

			if( order.OrderID == "Harvest" )
				return new Order(order.OrderID, self, queued) { TargetLocation = Util.CellContaining(target.CenterLocation) };

			return null;
		}

		public string VoicePhraseForOrder(Actor self, Order order)
		{
			return (order.OrderString == "Harvest" || (order.OrderString == "Deliver" && !IsEmpty)) ? "Move" : null;
		}
		
		public void ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString == "Harvest")
			{
				self.SetTargetLine(Target.FromOrder(order), Color.Red);
				
				var mobile = self.Trait<Mobile>();
				self.CancelActivity();
				self.QueueActivity(mobile.MoveTo(order.TargetLocation, 0));
				self.QueueActivity(new Harvest());
			}
			else if (order.OrderString == "Deliver")
			{
				if (order.TargetActor != LinkedProc)
					LinkedProc = order.TargetActor;
				
				if (IsEmpty)
					return;
				
				self.SetTargetLine(Target.FromOrder(order), Color.Green);

				self.CancelActivity();
				self.QueueActivity(new DeliverResources());
			}
		}
		
		public void LinkProc(Actor self, Actor proc)
		{
			LinkedProc = proc;
		}
		
		public void UnlinkProc(Actor self, Actor proc)
		{
			if (LinkedProc != proc)
				return;

			ChooseNewProc(self, proc);
		}

		PipType GetPipAt(int i)
		{
			var n = i * Info.Capacity / Info.PipCount;

			foreach (var rt in contents)
				if (n < rt.Value)
					return rt.Key.PipColor;
				else
					n -= rt.Value;

			return PipType.Transparent;
		}
		
		public IEnumerable<PipType> GetPips(Actor self)
		{
			int numPips = Info.PipCount;

			for (int i = 0; i < numPips; i++)
				yield return GetPipAt(i);
		}
				
		public IEnumerable<Renderable> ModifyRender(Actor self, IEnumerable<Renderable> r)
		{
			return Visible ? r : new Renderable[] { };
		}

		public bool ShouldExplode(Actor self) { return !IsEmpty; }

		public decimal GetSpeedModifier()
		{
			return 1m - ( 1m - Info.FullyLoadedSpeed ) * contents.Values.Sum() / Info.Capacity;
		}

		class HarvestOrderTargeter : IOrderTargeter
		{
			public string OrderID { get { return "Harvest";}}
			public int OrderPriority { get { return 10; } }

			public bool CanTargetActor(Actor self, Actor target, bool forceAttack, bool forceMove, bool forceQueued, ref string cursor)
			{
				return false;
			}

			public bool CanTargetLocation(Actor self, int2 location, List<Actor> actorsAtLocation, bool forceAttack, bool forceMove, bool forceQueued, ref string cursor)
			{
				// Don't leak info about resources under the shroud
				if (!self.World.LocalShroud.IsExplored(location)) return false;

				var res = self.World.WorldActor.Trait<ResourceLayer>().GetResource( location );
				var info = self.Info.Traits.Get<HarvesterInfo>();

				if( res == null ) return false;
				if( !info.Resources.Contains( res.info.Name ) ) return false;
				cursor = "attackmove";
				IsQueued = forceQueued;

				return true;
			}
			public bool IsQueued { get; protected set; }
		}
	}
}
