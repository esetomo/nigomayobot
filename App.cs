﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NigoMayo
{
    class App
    {
        private readonly Client m_client = new Client();

        public static void Main(string[] args)
        {
            Console.WriteLine("LocalApplicationData:{0}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

            if (args.Length == 3)
            {
                Properties.Settings.Default.FirstName = args[0];
                Properties.Settings.Default.LastName = args[1];
                Properties.Settings.Default.Password = args[2];
                Properties.Settings.Default.Save();
            }
            (new App()).Run();
        }

        public void Run()
        {
            SixamoCS.DEBUG = true;
	    // SixamoCS.Core sixamo = SixamoCS.Create("data");
	    // Console.WriteLine(sixamo.Talk());

            m_client.Login(Properties.Settings.Default.FirstName,
                           Properties.Settings.Default.LastName,
                           Properties.Settings.Default.Password);
            Console.ReadLine();
            m_client.Network.Logout();
        }
    }
}
