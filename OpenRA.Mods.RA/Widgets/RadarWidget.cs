#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion
using System;
using System.Drawing;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA.Widgets
{
	public class RadarWidget : Widget
	{
		public string WorldInteractionController = null;
		public int AnimationLength = 5;
		public string RadarOnlineSound = null;
		public string RadarOfflineSound = null;
		float radarMinimapHeight;
		int AnimationFrame = 0;
		bool hasRadar = false;
		
		float previewScale = 0;
		RectangleF mapRect = Rectangle.Empty;
		int2 previewOrigin;
		
		Sprite terrainSprite;
		Sprite customTerrainSprite;
		Sprite actorSprite;
		Sprite shroudSprite;

		readonly World world;
		[ObjectCreator.UseCtor]
		public RadarWidget( [ObjectCreator.Param] World world )
		{
			this.world = world;
		}
		
		public override void Initialize()
		{
			base.Initialize();

			var size = Math.Max(world.Map.Bounds.Width, world.Map.Bounds.Height);
			previewScale = Math.Min(RenderBounds.Width * 1f / world.Map.Bounds.Width, RenderBounds.Height * 1f / world.Map.Bounds.Height);
			previewOrigin = new int2(RenderOrigin.X, RenderOrigin.Y + (int)(previewScale * (size - world.Map.Bounds.Height)/2));
			mapRect = new RectangleF(previewOrigin.X, previewOrigin.Y, (int)(world.Map.Bounds.Width * previewScale), (int)(world.Map.Bounds.Height * previewScale));

			// Only needs to be done once
			var terrainBitmap = Minimap.TerrainBitmap(world.Map);
			var r = new Rectangle( 0, 0, world.Map.Bounds.Width, world.Map.Bounds.Height );
			var s = new Size( terrainBitmap.Width, terrainBitmap.Height );
			terrainSprite = new Sprite(new Sheet(s), r, TextureChannel.Alpha);
			terrainSprite.sheet.Texture.SetData(terrainBitmap);

			// Data is set in Tick()
			customTerrainSprite = new Sprite(new Sheet(s), r, TextureChannel.Alpha);
			actorSprite = new Sprite(new Sheet(s), r, TextureChannel.Alpha);
			shroudSprite = new Sprite(new Sheet(s), r, TextureChannel.Alpha);
		}
		
		public override string GetCursor(int2 pos)
		{		
			if (world == null || !hasRadar)
				return null;
						
			var loc = MinimapPixelToCell(pos);

			var mi = new MouseInput
			{
				Location = loc,
				Button = MouseButton.Right,
				Modifiers = Game.GetModifierKeys()
			};

			var cursor = world.OrderGenerator.GetCursor( world, loc, mi );
			if (cursor == null)
				return "default";
			
			return CursorProvider.HasCursorSequence(cursor+"-minimap") ? cursor+"-minimap" : cursor;
		}

		// TODO: RadarWidget doesn't support delegate methods for mouse input
		public override bool HandleMouseInput(MouseInput mi)
		{
			if (!hasRadar || Animating) return false;

			if (!mapRect.Contains(mi.Location))
				return false;

			var loc = MinimapPixelToCell(mi.Location);
			if ((mi.Event == MouseInputEvent.Down || mi.Event == MouseInputEvent.Move) && mi.Button == MouseButton.Left)
				Game.viewport.Center(loc);

			if (mi.Event == MouseInputEvent.Down && mi.Button == MouseButton.Right)
			{
				// fake a mousedown/mouseup here
				var fakemi = new MouseInput
				{
					Event = MouseInputEvent.Down,
					Button = MouseButton.Right,
					Modifiers = mi.Modifiers,
					Location = (loc * Game.CellSize - Game.viewport.Location).ToInt2()
				};

				if (WorldInteractionController != null)
				{
					var controller = Widget.RootWidget.GetWidget<WorldInteractionControllerWidget>(WorldInteractionController);
					controller.HandleMouseInput(fakemi);
					fakemi.Event = MouseInputEvent.Up;
					controller.HandleMouseInput(fakemi);
				}
			}

			return true;
		}

		public override Rectangle EventBounds
		{
			get { return new Rectangle((int)mapRect.X, (int)mapRect.Y, (int)mapRect.Width, (int)mapRect.Height);}
		}

		public override void DrawInner()
		{
			if( world == null || world.LocalPlayer == null ) return;

			var o = new float2(mapRect.Location.X, mapRect.Location.Y + world.Map.Bounds.Height * previewScale * (1 - radarMinimapHeight)/2);
			var s = new float2(mapRect.Size.Width, mapRect.Size.Height*radarMinimapHeight);
			Game.Renderer.RgbaSpriteRenderer.DrawSprite(terrainSprite, o, s);
			Game.Renderer.RgbaSpriteRenderer.DrawSprite(customTerrainSprite, o, s);
			Game.Renderer.RgbaSpriteRenderer.DrawSprite(actorSprite, o, s);
			Game.Renderer.RgbaSpriteRenderer.DrawSprite(shroudSprite, o, s);

			// Draw viewport rect
			if (hasRadar && !Animating)
			{
				var tl = CellToMinimapPixel(new int2((int)(Game.viewport.Location.X/Game.CellSize), (int)(Game.viewport.Location.Y/Game.CellSize)));
				var br = CellToMinimapPixel(new int2((int)((Game.viewport.Location.X + Game.viewport.Width)/Game.CellSize), (int)((Game.viewport.Location.Y + Game.viewport.Height)/Game.CellSize)));
				var tr = new int2(br.X, tl.Y);
				var bl = new int2(tl.X, br.Y);
				Game.Renderer.EnableScissor((int)mapRect.Left, (int)mapRect.Top, (int)mapRect.Width, (int)mapRect.Height);
				Game.Renderer.LineRenderer.DrawLine(Game.viewport.Location + tl, Game.viewport.Location + tr, Color.White, Color.White);
				Game.Renderer.LineRenderer.DrawLine(Game.viewport.Location + tr, Game.viewport.Location + br, Color.White, Color.White);
				Game.Renderer.LineRenderer.DrawLine(Game.viewport.Location + br, Game.viewport.Location + bl, Color.White, Color.White);
				Game.Renderer.LineRenderer.DrawLine(Game.viewport.Location + bl, Game.viewport.Location + tl, Color.White, Color.White);
				Game.Renderer.DisableScissor();
			}
		}
		
		bool Animating = false;
		int updateTicks = 0;
		public override void Tick()
		{
			var hasRadarNew = world
				.ActorsWithTrait<ProvidesRadar>()
				.Any(a => a.Actor.Owner == world.LocalPlayer && a.Trait.IsActive);
			
			if (hasRadarNew != hasRadar)
			{
				Animating = true;
				Sound.Play(hasRadarNew ? RadarOnlineSound : RadarOfflineSound);
			}
			hasRadar = hasRadarNew;
			
			// Build the radar image
			if (hasRadar)
			{
				--updateTicks;
				if (updateTicks <= 0)
				{
					updateTicks = 12;
					customTerrainSprite.sheet.Texture.SetData(Minimap.CustomTerrainBitmap(world));
				}
				
				if (updateTicks == 8)
					actorSprite.sheet.Texture.SetData(Minimap.ActorsBitmap(world));
				
				if (updateTicks == 4)
					shroudSprite.sheet.Texture.SetData(Minimap.ShroudBitmap(world));
			}
			
			if (!Animating)
				return;
			
			// Increment frame
			if (hasRadar)
				AnimationFrame++;
			else
				AnimationFrame--;

			// Minimap height
			radarMinimapHeight = float2.Lerp(0, 1, AnimationFrame*1.0f / AnimationLength);

			// Animation is complete
			if (AnimationFrame == (hasRadar ? AnimationLength : 0))
				Animating = false;
		}
		
		int2 CellToMinimapPixel(int2 p)
		{
			return new int2((int)(mapRect.X + previewScale*(p.X - world.Map.Bounds.Left)), (int)(mapRect.Y + previewScale*(p.Y - world.Map.Bounds.Top)));
		}
		
		int2 MinimapPixelToCell(int2 p)
		{
			return new int2(world.Map.Bounds.Left + (int)((p.X - mapRect.X)/previewScale), world.Map.Bounds.Top + (int)((p.Y - mapRect.Y)/previewScale));
		}
	}
}
