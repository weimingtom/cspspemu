﻿using CSPspEmu.Hle.Formats;
using NUnit.Framework;
using System;
using System.IO;

namespace CSPspEmu.Core.Tests
{
	[TestFixture]
	public class PbpTest
	{
		[Test]
		public void LoadTest()
		{
			var Pbp = new Pbp();
			Pbp.Load(File.OpenRead("../../../TestInput/HelloJpcsp.pbp"));
		}
	}
}