﻿using System;
using System.IO;

namespace ZMachine.Tests
{
    public class ZMachineLoader
    {
        static public ZMachine Load(string filename)
        {
            Console.WriteLine("Loading " + filename);
            var zm = new ZMachine();
            using (var stream = File.OpenRead(filename))
            {
                zm.LoadStory(stream);
            }
            Console.WriteLine("Loaded " + filename);

            Console.WriteLine($"ZMachine Version {zm.MainMemory.Header.Version}");
            return zm;
        }
    }
}
