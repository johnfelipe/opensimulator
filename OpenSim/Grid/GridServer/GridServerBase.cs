/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Timers;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Grid.Framework;

namespace OpenSim.Grid.GridServer
{
    /// <summary>
    /// </summary>
    public class GridServerBase : BaseOpenSimServer, IGridServiceCore
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected GridConfig m_gconfig;
        protected UserConfig m_uconfig;

        protected GridConfigurationLoader m_configLoader;
        protected OpenSimConfigSource m_configSource;

        public OpenSimConfigSource ConfigSource
        {
            get { return m_configSource; }
        }

        public GridConfig GConfig
        {
            get { return m_gconfig; }
        }

        public UserConfig UConfig
        {
            get { return m_uconfig; }
        }

        public string Version
        {
            get { return m_version; }
        }

        protected List<IGridPlugin> m_plugins = new List<IGridPlugin>();

        public void Work()
        {
            m_console.Notice("Enter help for a list of commands\n");

            while (true)
            {
                m_console.Prompt();
            }
        }

        public GridServerBase(IConfigSource configSource)
        {
            m_console = new ConsoleBase("Grid");
            MainConsole.Instance = m_console;

            m_configLoader = new GridConfigurationLoader();
            m_configSource = m_configLoader.LoadConfigSettings(configSource);
        }

        protected override void StartupSpecific()
        {
            uint httpPort = 8051;

           // m_gconfig = new GridConfig("GRID SERVER", (Path.Combine(Util.configDir(), "GridServer_Config.xml")));

            //m_uconfig = new UserConfig("USER SERVER", (Path.Combine(Util.configDir(), "UserServer_Config.xml")));

            m_uconfig = new UserConfig();
            m_uconfig.LoadConfigurationFromNini(m_configSource.Source);

            m_gconfig = new GridConfig();
            m_gconfig.LoadConfigurationFromNini(m_configSource.Source);

            Console.WriteLine("grid server  database provider is {0} ", m_gconfig.DatabaseProvider);

            IConfig startupConfig = m_configSource.Source.Configs["Startup"];
            if (startupConfig != null)
            {
                httpPort = Convert.ToUInt32(startupConfig.GetString("HttpPort", "8051"));

                m_log.InfoFormat("[GRID]: Starting HTTP process on port {0}", httpPort);
                m_httpServer = new BaseHttpServer(httpPort);
                RegisterInterface<BaseHttpServer>(m_httpServer);

                string pluginsToLoad = startupConfig.GetString("LoadPlugins", "");

                if (!String.IsNullOrEmpty(pluginsToLoad))
                {
                    LoadPlugins(pluginsToLoad);
                }
                else
                {
                    LoadPlugins();
                }

                m_httpServer.Start();

            }
           
           
            base.StartupSpecific();
        }

        protected virtual void LoadPlugins()
        {
            PluginLoader<IGridPlugin> loader =
                new PluginLoader<IGridPlugin>(new GridPluginInitialiser(this));

            loader.Load("/OpenSim/GridServer");
            m_plugins = loader.Plugins;
        }

        protected virtual void LoadPlugins(string pluginNames)
        {
            PluginLoader<IGridPlugin> loader =
                new PluginLoader<IGridPlugin>(new GridPluginInitialiser(this));

            PluginIdFilter filer = new PluginIdFilter(pluginNames);
            loader.Add("/OpenSim/GridServer", filer);

            loader.Load();
            m_plugins = loader.Plugins;
        }

        public override void ShutdownSpecific()
        {
            foreach (IGridPlugin plugin in m_plugins) plugin.Dispose();
        }

        #region IServiceCore
        protected Dictionary<Type, object> m_moduleInterfaces = new Dictionary<Type, object>();

        /// <summary>
        /// Register an Module interface.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="iface"></param>
        public void RegisterInterface<T>(T iface)
        {
            lock (m_moduleInterfaces)
            {
                if (!m_moduleInterfaces.ContainsKey(typeof(T)))
                {
                    m_moduleInterfaces.Add(typeof(T), iface);
                }
            }
        }

        public bool TryGet<T>(out T iface)
        {
            if (m_moduleInterfaces.ContainsKey(typeof(T)))
            {
                iface = (T)m_moduleInterfaces[typeof(T)];
                return true;
            }
            iface = default(T);
            return false;
        }

        public T Get<T>()
        {
            return (T)m_moduleInterfaces[typeof(T)];
        }

        public BaseHttpServer GetHttpServer()
        {
            return m_httpServer;
        }
        #endregion
    }
}
