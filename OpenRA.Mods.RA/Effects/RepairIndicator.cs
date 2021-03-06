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
using OpenRA.Effects;
using OpenRA.Graphics;
using OpenRA.Mods.RA.Buildings;
using OpenRA.Traits;

namespace OpenRA.Mods.RA.Effects
{
	class RepairIndicator : IEffect
	{
		int framesLeft;
		Actor a;
		Animation anim = new Animation("select");

		public RepairIndicator(Actor a, int frames) 
		{ 
			this.a = a; anim.PlayRepeating("repair");
            framesLeft = frames;
		}

		public void Tick( World world )
		{
			if (--framesLeft == 0 || !a.IsInWorld || a.IsDead())
				world.AddFrameEndTask(w => w.Remove(this));
		}

		public IEnumerable<Renderable> Render()
		{
			if (!a.Destroyed)
				yield return new Renderable(anim.Image, 
					a.CenterLocation - .5f * anim.Image.size, "chrome", (int)a.CenterLocation.Y);
		}
	}
}
