﻿#region Copyright & License Information
/*
 * Copyright 2007-2011 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using OpenRA.FileFormats;
using OpenRA.Widgets;

namespace OpenRA.Server
{
    public static class MasterServerQuery
	{
		public static event Action<GameServer[]> OnComplete = _ => { };
		public static event Action<string> OnVersion = _ => { };

		static GameServer[] Games = { };
		public static string ClientVersion = "";
		public static string ServerVersion = "";
		static AutoResetEvent ev = new AutoResetEvent(false);
		static AutoResetEvent ev2 = new AutoResetEvent(false);

		public static void Refresh(string masterServerUrl)
		{
			new Thread(() =>
			{
				try
				{
					var str = GetData(new Uri(masterServerUrl + "list.php"));

					var yaml = MiniYaml.FromString(str);

					Games = yaml.Select(a => FieldLoader.Load<GameServer>(a.Value))
						.Where(gs => gs.Address != null).ToArray();
				}
				catch
				{
					Games = null;
				}

				ev.Set();
			}).Start();
		}

		public static void Tick()
		{
			if (ev.WaitOne(TimeSpan.FromMilliseconds(0))) 
				OnComplete(Games);
			if (ev2.WaitOne(TimeSpan.FromMilliseconds(0)))
				OnVersion(ServerVersion);
		}

		static string GetData(Uri uri)
		{
			var wc = new WebClient();
			wc.Proxy = null;
			var data = wc.DownloadData(uri);
			return Encoding.UTF8.GetString(data);
		}
	}

    public class GameServer
	{
		public readonly int Id = 0;
		public readonly string Name = null;
		public readonly string Address = null;
		public readonly int State = 0;
		public readonly int Players = 0;
		public readonly string Map = null;
		public readonly string[] Mods = { }; 
		public readonly int TTL = 0;
		
		public Dictionary<string, string> UsefulMods {
			get {
				return Mods.Where(v => v.Contains('@')).ToDictionary(v => v.Split('@')[0], v => v.Split('@')[1]);
			}	
		}
	}
}
