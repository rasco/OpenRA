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
using System.Linq;
using OpenRA.Traits;
using OpenRA.Graphics;

namespace OpenRA.Mods.Cnc
{
	public class WithRoofInfo : ITraitInfo, ITraitPrerequisite<RenderSimpleInfo>
	{
		public object Create(ActorInitializer init) { return new WithRoof(init.self); }
	}

	public class WithRoof
	{
		public WithRoof(Actor self)
		{
			var rs = self.Trait<RenderSimple>();
			var roof = new Animation(rs.GetImage(self), () => self.Trait<IFacing>().Facing);
			roof.Play("roof");
			rs.anims.Add( "roof", new RenderSimple.AnimationWithOffset( roof ) { ZOffset = 24 } );
		}
	}
}
