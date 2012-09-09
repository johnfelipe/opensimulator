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
 *     * Neither the name of the OpenSimulator Project nor the
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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Lifetime;
using System.Text;
using System.Net;
using System.Threading;
using System.Xml;
using log4net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Nini.Config;
using OpenSim;
using OpenSim.Framework;

using OpenSim.Framework.Console;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api.Plugins;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.Api.Interfaces;
using TPFlags = OpenSim.Framework.Constants.TeleportFlags;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using System.Text.RegularExpressions;

using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;

namespace OpenSim.Region.ScriptEngine.Shared.Api
{
    //////////////////////////////////////////////////////////////
    //
    // Level description
    //
    // None     - Function is no threat at all. It doesn't constitute
    //            an threat to either users or the system and has no
    //            known side effects
    //
    // Nuisance - Abuse of this command can cause a nuisance to the
    //            region operator, such as log message spew
    //
    // VeryLow  - Extreme levels ob abuse of this function can cause
    //            impaired functioning of the region, or very gullible
    //            users can be tricked into experiencing harmless effects
    //
    // Low      - Intentional abuse can cause crashes or malfunction
    //            under certain circumstances, which can easily be rectified,
    //            or certain users can be tricked into certain situations
    //            in an avoidable manner.
    //
    // Moderate - Intentional abuse can cause denial of service and crashes
    //            with potential of data or state loss, or trusting users
    //            can be tricked into embarrassing or uncomfortable
    //            situationsa.
    //
    // High     - Casual abuse can cause impaired functionality or temporary
    //            denial of service conditions. Intentional abuse can easily
    //            cause crashes with potential data loss, or can be used to
    //            trick experienced and cautious users into unwanted situations,
    //            or changes global data permanently and without undo ability
    //            Malicious scripting can allow theft of content
    //
    // VeryHigh - Even normal use may, depending on the number of instances,
    //            or frequency of use, result in severe service impairment
    //            or crash with loss of data, or can be used to cause
    //            unwanted or harmful effects on users without giving the
    //            user a means to avoid it.
    //
    // Severe   - Even casual use is a danger to region stability, or function
    //            allows console or OS command execution, or function allows
    //            taking money without consent, or allows deletion or
    //            modification of user data, or allows the compromise of
    //            sensitive data by design.

    class FunctionPerms
    {
        public List<UUID> AllowedCreators;
        public List<UUID> AllowedOwners;
        public List<string> AllowedOwnerClasses;

        public FunctionPerms()
        {
            AllowedCreators = new List<UUID>();
            AllowedOwners = new List<UUID>();
            AllowedOwnerClasses = new List<string>();
        }
    }

    [Serializable]
    public class OSSL_Api : MarshalByRefObject, IOSSL_Api, IScriptApi
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public const string GridInfoServiceConfigSectionName = "GridInfoService";

        internal IScriptEngine m_ScriptEngine;
        internal ILSL_Api m_LSL_Api = null; // get a reference to the LSL API so we can call methods housed there
        internal SceneObjectPart m_host;
        internal TaskInventoryItem m_item;
        internal bool m_OSFunctionsEnabled = false;
        internal ThreatLevel m_MaxThreatLevel = ThreatLevel.VeryLow;
        internal float m_ScriptDelayFactor = 1.0f;
        internal float m_ScriptDistanceFactor = 1.0f;
        internal Dictionary<string, FunctionPerms > m_FunctionPerms = new Dictionary<string, FunctionPerms >();

        protected IUrlModule m_UrlModule = null;

        public void Initialize(IScriptEngine ScriptEngine, SceneObjectPart host, TaskInventoryItem item)
        {
            m_ScriptEngine = ScriptEngine;
            m_host = host;
            m_item = item;

            m_UrlModule = m_ScriptEngine.World.RequestModuleInterface<IUrlModule>();

            if (m_ScriptEngine.Config.GetBoolean("AllowOSFunctions", false))
                m_OSFunctionsEnabled = true;

            m_ScriptDelayFactor =
                    m_ScriptEngine.Config.GetFloat("ScriptDelayFactor", 1.0f);
            m_ScriptDistanceFactor =
                    m_ScriptEngine.Config.GetFloat("ScriptDistanceLimitFactor", 1.0f);

            string risk = m_ScriptEngine.Config.GetString("OSFunctionThreatLevel", "VeryLow");
            switch (risk)
            {
            case "NoAccess":
                m_MaxThreatLevel = ThreatLevel.NoAccess;
                break;
            case "None":
                m_MaxThreatLevel = ThreatLevel.None;
                break;
            case "VeryLow":
                m_MaxThreatLevel = ThreatLevel.VeryLow;
                break;
            case "Low":
                m_MaxThreatLevel = ThreatLevel.Low;
                break;
            case "Moderate":
                m_MaxThreatLevel = ThreatLevel.Moderate;
                break;
            case "High":
                m_MaxThreatLevel = ThreatLevel.High;
                break;
            case "VeryHigh":
                m_MaxThreatLevel = ThreatLevel.VeryHigh;
                break;
            case "Severe":
                m_MaxThreatLevel = ThreatLevel.Severe;
                break;
            default:
                break;
            }
        }

        public override Object InitializeLifetimeService()
        {
            ILease lease = (ILease)base.InitializeLifetimeService();

            if (lease.CurrentState == LeaseState.Initial)
            {
                lease.InitialLeaseTime = TimeSpan.FromMinutes(0);
//                lease.RenewOnCallTime = TimeSpan.FromSeconds(10.0);
//                lease.SponsorshipTimeout = TimeSpan.FromMinutes(1.0);
            }
            return lease;
        }

        public Scene World
        {
            get { return m_ScriptEngine.World; }
        }

        internal void OSSLError(string msg)
        {
            throw new Exception("OSSL Runtime Error: " + msg);
        }

        /// <summary>
        /// Initialize the LSL interface.
        /// </summary>
        /// <remarks>
        /// FIXME: This is an abomination.  We should be able to set this up earlier but currently we have no
        /// guarantee the interface is present on Initialize().  There needs to be another post initialize call from
        /// ScriptInstance.
        /// </remarks>
        private void InitLSL()
        {
            if (m_LSL_Api != null)
                return;

            m_LSL_Api = (ILSL_Api)m_ScriptEngine.GetApi(m_item.ItemID, "LSL");
        }

        //
        //Dumps an error message on the debug console.
        //

        internal void OSSLShoutError(string message)
        {
            if (message.Length > 1023)
                message = message.Substring(0, 1023);

            World.SimChat(Utils.StringToBytes(message),
                          ChatTypeEnum.Shout, ScriptBaseClass.DEBUG_CHANNEL, m_host.ParentGroup.RootPart.AbsolutePosition, m_host.Name, m_host.UUID, true);

            IWorldComm wComm = m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
            wComm.DeliverMessage(ChatTypeEnum.Shout, ScriptBaseClass.DEBUG_CHANNEL, m_host.Name, m_host.UUID, message);
        }

        public void CheckThreatLevel(ThreatLevel level, string function)
        {
            if (!m_OSFunctionsEnabled)
                OSSLError(String.Format("{0} permission denied.  All OS functions are disabled.", function)); // throws

            if (!m_FunctionPerms.ContainsKey(function))
            {
                FunctionPerms perms = new FunctionPerms();
                m_FunctionPerms[function] = perms;

                string ownerPerm = m_ScriptEngine.Config.GetString("Allow_" + function, "");
                string creatorPerm = m_ScriptEngine.Config.GetString("Creators_" + function, "");
                if (ownerPerm == "" && creatorPerm == "")
                {
                    // Default behavior
                    perms.AllowedOwners = null;
                    perms.AllowedCreators = null;
                    perms.AllowedOwnerClasses = null;
                }
                else
                {
                    bool allowed;

                    if (bool.TryParse(ownerPerm, out allowed))
                    {
                        // Boolean given
                        if (allowed)
                        {
                            // Allow globally
                            perms.AllowedOwners.Add(UUID.Zero);
                        }
                    }
                    else
                    {
                        string[] ids = ownerPerm.Split(new char[] {','});
                        foreach (string id in ids)
                        {
                            string current = id.Trim();
                            if (current.ToUpper() == "PARCEL_GROUP_MEMBER" || current.ToUpper() == "PARCEL_OWNER" || current.ToUpper() == "ESTATE_MANAGER" || current.ToUpper() == "ESTATE_OWNER")
                            {
                                if (!perms.AllowedOwnerClasses.Contains(current))
                                    perms.AllowedOwnerClasses.Add(current.ToUpper());
                            }
                            else
                            {
                                UUID uuid;

                                if (UUID.TryParse(current, out uuid))
                                {
                                    if (uuid != UUID.Zero)
                                        perms.AllowedOwners.Add(uuid);
                                }
                            }
                        }

                        ids = creatorPerm.Split(new char[] {','});
                        foreach (string id in ids)
                        {
                            string current = id.Trim();
                            UUID uuid;

                            if (UUID.TryParse(current, out uuid))
                            {
                                if (uuid != UUID.Zero)
                                    perms.AllowedCreators.Add(uuid);
                            }
                        }
                    }
                }
            }

            // If the list is null, then the value was true / undefined
            // Threat level governs permissions in this case
            //
            // If the list is non-null, then it is a list of UUIDs allowed
            // to use that particular function. False causes an empty
            // list and therefore means "no one"
            //
            // To allow use by anyone, the list contains UUID.Zero
            //
            if (m_FunctionPerms[function].AllowedOwners == null)
            {
                // Allow / disallow by threat level
                if (level > m_MaxThreatLevel)
                    OSSLError(
                        String.Format(
                            "{0} permission denied.  Allowed threat level is {1} but function threat level is {2}.",
                            function, m_MaxThreatLevel, level));
            }
            else
            {
                if (!m_FunctionPerms[function].AllowedOwners.Contains(UUID.Zero))
                {
                    // Not anyone. Do detailed checks
                    if (m_FunctionPerms[function].AllowedOwners.Contains(m_host.OwnerID))
                    {
                        // prim owner is in the list of allowed owners
                        return;
                    }

                    UUID ownerID = m_item.OwnerID;

                    //OSSL only may be used if object is in the same group as the parcel
                    if (m_FunctionPerms[function].AllowedOwnerClasses.Contains("PARCEL_GROUP_MEMBER"))
                    {
                        ILandObject land = World.LandChannel.GetLandObject(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y);

                        if (land.LandData.GroupID == m_item.GroupID && land.LandData.GroupID != UUID.Zero)
                        {
                            return;
                        }
                    }

                    //Only Parcelowners may use the function
                    if (m_FunctionPerms[function].AllowedOwnerClasses.Contains("PARCEL_OWNER"))
                    {
                        ILandObject land = World.LandChannel.GetLandObject(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y);

                        if (land.LandData.OwnerID == ownerID)
                        {
                            return;
                        }
                    }

                    //Only Estate Managers may use the function
                    if (m_FunctionPerms[function].AllowedOwnerClasses.Contains("ESTATE_MANAGER"))
                    {
                        //Only Estate Managers may use the function
                        if (World.RegionInfo.EstateSettings.IsEstateManagerOrOwner(ownerID) && World.RegionInfo.EstateSettings.EstateOwner != ownerID)
                        {
                            return;
                        }
                    }

                    //Only regionowners may use the function
                    if (m_FunctionPerms[function].AllowedOwnerClasses.Contains("ESTATE_OWNER"))
                    {
                        if (World.RegionInfo.EstateSettings.EstateOwner == ownerID)
                        {
                            return;
                        }
                    }

                    if (!m_FunctionPerms[function].AllowedCreators.Contains(m_item.CreatorID))
                        OSSLError(
                            String.Format("{0} permission denied. Script creator is not in the list of users allowed to execute this function and prim owner also has no permission.",
                            function));

                    if (m_item.CreatorID != ownerID)
                    {
                        if ((m_item.CurrentPermissions & (uint)PermissionMask.Modify) != 0)
                            OSSLError(
                                String.Format("{0} permission denied. Script permissions error.",
                                function));

                    }
                }
            }
        }

        internal void OSSLDeprecated(string function, string replacement)
        {
            OSSLShoutError(string.Format("Use of function {0} is deprecated. Use {1} instead.", function, replacement));
        }

        protected void ScriptSleep(int delay)
        {
            delay = (int)((float)delay * m_ScriptDelayFactor);
            if (delay == 0)
                return;
            System.Threading.Thread.Sleep(delay);
        }

        public LSL_Integer osSetTerrainHeight(int x, int y, double val)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetTerrainHeight");

            return SetTerrainHeight(x, y, val);
        }

        public LSL_Integer osTerrainSetHeight(int x, int y, double val)
        {
            CheckThreatLevel(ThreatLevel.High, "osTerrainSetHeight");
            OSSLDeprecated("osTerrainSetHeight", "osSetTerrainHeight");

            return SetTerrainHeight(x, y, val);
        }

        private LSL_Integer SetTerrainHeight(int x, int y, double val)
        {
            m_host.AddScriptLPS(1);

            if (x > ((int)Constants.RegionSize - 1) || x < 0 || y > ((int)Constants.RegionSize - 1) || y < 0)
                OSSLError("osSetTerrainHeight: Coordinate out of bounds");

            if (World.Permissions.CanTerraformLand(m_host.OwnerID, new Vector3(x, y, 0)))
            {
                World.Heightmap[x, y] = val;
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public LSL_Float osGetTerrainHeight(int x, int y)
        {
            CheckThreatLevel(ThreatLevel.None, "osGetTerrainHeight");
            return GetTerrainHeight(x, y);
        }

        public LSL_Float osTerrainGetHeight(int x, int y)
        {
            CheckThreatLevel(ThreatLevel.None, "osTerrainGetHeight");
            OSSLDeprecated("osTerrainGetHeight", "osGetTerrainHeight");
            return GetTerrainHeight(x, y);
        }

        private LSL_Float GetTerrainHeight(int x, int y)
        {
            m_host.AddScriptLPS(1);
            if (x > ((int)Constants.RegionSize - 1) || x < 0 || y > ((int)Constants.RegionSize - 1) || y < 0)
                OSSLError("osGetTerrainHeight: Coordinate out of bounds");

            return World.Heightmap[x, y];
        }

        public void osTerrainFlush()
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osTerrainFlush");
            m_host.AddScriptLPS(1);

            ITerrainModule terrainModule = World.RequestModuleInterface<ITerrainModule>();
            if (terrainModule != null) terrainModule.TaintTerrain();
        }

        public int osRegionRestart(double seconds)
        {
            // This is High here because region restart is not reliable
            // it may result in the region staying down or becoming
            // unstable. This should be changed to Low or VeryLow once
            // The underlying functionality is fixed, since the security
            // as such is sound
            //
            CheckThreatLevel(ThreatLevel.High, "osRegionRestart");

            IRestartModule restartModule = World.RequestModuleInterface<IRestartModule>();
            m_host.AddScriptLPS(1);
            if (World.Permissions.CanIssueEstateCommand(m_host.OwnerID, false) && (restartModule != null))
            {
                if (seconds < 15)
                {
                    restartModule.AbortRestart("Restart aborted");
                    return 1;
                }

                List<int> times = new List<int>();
                while (seconds > 0)
                {
                    times.Add((int)seconds);
                    if (seconds > 300)
                        seconds -= 120;
                    else if (seconds > 30)
                        seconds -= 30;
                    else
                        seconds -= 15;
                }

                restartModule.ScheduleRestart(UUID.Zero, "Region will restart in {0}", times.ToArray(), true);
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public void osRegionNotice(string msg)
        {
            // This implementation provides absolutely no security
            // It's high griefing potential makes this classification
            // necessary
            //
            CheckThreatLevel(ThreatLevel.VeryHigh, "osRegionNotice");

            m_host.AddScriptLPS(1);

            IDialogModule dm = World.RequestModuleInterface<IDialogModule>();

            if (dm != null)
                dm.SendGeneralAlert(msg);
        }

        public void osSetRot(UUID target, Quaternion rotation)
        {
            // This function has no security. It can be used to destroy
            // arbitrary builds the user would normally have no rights to
            //
            CheckThreatLevel(ThreatLevel.VeryHigh, "osSetRot");

            m_host.AddScriptLPS(1);
            if (World.Entities.ContainsKey(target))
            {
                EntityBase entity;
                if (World.Entities.TryGetValue(target, out entity))
                {
                    if (entity is SceneObjectGroup)
                        ((SceneObjectGroup)entity).UpdateGroupRotationR(rotation);
                    else if (entity is ScenePresence)
                        ((ScenePresence)entity).Rotation = rotation;
                }
            }
            else
            {
                OSSLError("osSetRot: Invalid target");
            }
        }

        public string osSetDynamicTextureURL(string dynamicID, string contentType, string url, string extraParams,
                                             int timer)
        {
            // This may be upgraded depending on the griefing or DOS
            // potential, or guarded with a delay
            //
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetDynamicTextureURL");

            m_host.AddScriptLPS(1);
            if (dynamicID == String.Empty)
            {
                IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
                UUID createdTexture =
                    textureManager.AddDynamicTextureURL(World.RegionInfo.RegionID, m_host.UUID, contentType, url,
                                                        extraParams, timer);
                return createdTexture.ToString();
            }
            else
            {
                //TODO update existing dynamic textures
            }

            return UUID.Zero.ToString();
        }

        public string osSetDynamicTextureURLBlend(string dynamicID, string contentType, string url, string extraParams,
                                             int timer, int alpha)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetDynamicTextureURLBlend");

            m_host.AddScriptLPS(1);
            if (dynamicID == String.Empty)
            {
                IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
                UUID createdTexture =
                    textureManager.AddDynamicTextureURL(World.RegionInfo.RegionID, m_host.UUID, contentType, url,
                                                        extraParams, timer, true, (byte) alpha);
                return createdTexture.ToString();
            }
            else
            {
                //TODO update existing dynamic textures
            }

            return UUID.Zero.ToString();
        }

        public string osSetDynamicTextureURLBlendFace(string dynamicID, string contentType, string url, string extraParams,
                                             bool blend, int disp, int timer, int alpha, int face)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetDynamicTextureURLBlendFace");

            m_host.AddScriptLPS(1);
            if (dynamicID == String.Empty)
            {
                IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
                UUID createdTexture =
                    textureManager.AddDynamicTextureURL(World.RegionInfo.RegionID, m_host.UUID, contentType, url,
                                                        extraParams, timer, blend, disp, (byte) alpha, face);
                return createdTexture.ToString();
            }
            else
            {
                //TODO update existing dynamic textures
            }

            return UUID.Zero.ToString();
        }

        public string osSetDynamicTextureData(string dynamicID, string contentType, string data, string extraParams,
                                           int timer)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetDynamicTextureData");

            m_host.AddScriptLPS(1);
            if (dynamicID == String.Empty)
            {
                IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
                if (textureManager != null)
                {
                    if (extraParams == String.Empty)
                    {
                        extraParams = "256";
                    }
                    UUID createdTexture =
                        textureManager.AddDynamicTextureData(World.RegionInfo.RegionID, m_host.UUID, contentType, data,
                                                            extraParams, timer);
                    return createdTexture.ToString();
                }
            }
            else
            {
                //TODO update existing dynamic textures
            }

            return UUID.Zero.ToString();
        }

        public string osSetDynamicTextureDataBlend(string dynamicID, string contentType, string data, string extraParams,
                                          int timer, int alpha)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetDynamicTextureDataBlend");

            m_host.AddScriptLPS(1);
            if (dynamicID == String.Empty)
            {
                IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
                if (textureManager != null)
                {
                    if (extraParams == String.Empty)
                    {
                        extraParams = "256";
                    }
                    UUID createdTexture =
                        textureManager.AddDynamicTextureData(World.RegionInfo.RegionID, m_host.UUID, contentType, data,
                                                            extraParams, timer, true, (byte) alpha);
                    return createdTexture.ToString();
                }
            }
            else
            {
                //TODO update existing dynamic textures
            }

            return UUID.Zero.ToString();
        }

        public string osSetDynamicTextureDataBlendFace(string dynamicID, string contentType, string data, string extraParams,
                                          bool blend, int disp, int timer, int alpha, int face)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetDynamicTextureDataBlendFace");

            m_host.AddScriptLPS(1);
            if (dynamicID == String.Empty)
            {
                IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
                if (textureManager != null)
                {
                    if (extraParams == String.Empty)
                    {
                        extraParams = "256";
                    }
                    UUID createdTexture =
                        textureManager.AddDynamicTextureData(World.RegionInfo.RegionID, m_host.UUID, contentType, data,
                                                            extraParams, timer, blend, disp, (byte) alpha, face);
                    return createdTexture.ToString();
                }
            }
            else
            {
                //TODO update existing dynamic textures
            }

            return UUID.Zero.ToString();
        }

        public bool osConsoleCommand(string command)
        {
            CheckThreatLevel(ThreatLevel.Severe, "osConsoleCommand");

            m_host.AddScriptLPS(1);

            // For safety, we add another permission check here, and don't rely only on the standard OSSL permissions
            if (World.Permissions.CanRunConsoleCommand(m_host.OwnerID))
            {
                MainConsole.Instance.RunCommand(command);
                return true;
            }

            return false;
        }

        public void osSetPrimFloatOnWater(int floatYN)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetPrimFloatOnWater");

            m_host.AddScriptLPS(1);

            m_host.ParentGroup.RootPart.SetFloatOnWater(floatYN);
        }

        // Teleport functions
        public void osTeleportAgent(string agent, string regionName, LSL_Types.Vector3 position, LSL_Types.Vector3 lookat)
        {
            // High because there is no security check. High griefer potential
            //
            CheckThreatLevel(ThreatLevel.Severe, "osTeleportAgent");

            TeleportAgent(agent, regionName, position, lookat, false);
        }

        private void TeleportAgent(string agent, string regionName,
            LSL_Types.Vector3 position, LSL_Types.Vector3 lookat, bool relaxRestrictions)
        {
            m_host.AddScriptLPS(1);
            UUID agentId = new UUID();
            if (UUID.TryParse(agent, out agentId))
            {
                ScenePresence presence = World.GetScenePresence(agentId);
                if (presence != null)
                {
                    // For osTeleportAgent, agent must be over owners land to avoid abuse
                    // For osTeleportOwner, this restriction isn't necessary

                    // commented out because its redundant and uneeded please remove eventually.
                    // if (relaxRestrictions ||
                    //    m_host.OwnerID
                    //    == World.LandChannel.GetLandObject(
                    //        presence.AbsolutePosition.X, presence.AbsolutePosition.Y).LandData.OwnerID)
                    // {

                        // We will launch the teleport on a new thread so that when the script threads are terminated
                        // before teleport in ScriptInstance.GetXMLState(), we don't end up aborting the one doing the teleporting.                        
                        Util.FireAndForget(o => World.RequestTeleportLocation(
                            presence.ControllingClient, regionName, position,
                            lookat, (uint)TPFlags.ViaLocation));

                        ScriptSleep(5000);

                    // }

                }
            }
        }

        public void osTeleportAgent(string agent, int regionX, int regionY, LSL_Types.Vector3 position, LSL_Types.Vector3 lookat)
        {
            // High because there is no security check. High griefer potential
            //
            CheckThreatLevel(ThreatLevel.Severe, "osTeleportAgent");

            TeleportAgent(agent, regionX, regionY, position, lookat, false);
        }

        private void TeleportAgent(string agent, int regionX, int regionY,
            LSL_Types.Vector3 position, LSL_Types.Vector3 lookat, bool relaxRestrictions)
        {
            ulong regionHandle = Util.UIntsToLong(((uint)regionX * (uint)Constants.RegionSize), ((uint)regionY * (uint)Constants.RegionSize));

            m_host.AddScriptLPS(1);
            UUID agentId = new UUID();
            if (UUID.TryParse(agent, out agentId))
            {
                ScenePresence presence = World.GetScenePresence(agentId);
                if (presence != null)
                {
                    // For osTeleportAgent, agent must be over owners land to avoid abuse
                    // For osTeleportOwner, this restriction isn't necessary

                    // commented out because its redundant and uneeded please remove eventually.
                    // if (relaxRestrictions ||
                    //    m_host.OwnerID
                    //    == World.LandChannel.GetLandObject(
                    //        presence.AbsolutePosition.X, presence.AbsolutePosition.Y).LandData.OwnerID)
                    // {

                        // We will launch the teleport on a new thread so that when the script threads are terminated
                        // before teleport in ScriptInstance.GetXMLState(), we don't end up aborting the one doing the teleporting.
                        Util.FireAndForget(o => World.RequestTeleportLocation(
                            presence.ControllingClient, regionHandle, 
                            position, lookat, (uint)TPFlags.ViaLocation));

                        ScriptSleep(5000);

                   //  }

                }
            }
        }

        public void osTeleportAgent(string agent, LSL_Types.Vector3 position, LSL_Types.Vector3 lookat)
        {
            osTeleportAgent(agent, World.RegionInfo.RegionName, position, lookat);
        }

        public void osTeleportOwner(string regionName, LSL_Types.Vector3 position, LSL_Types.Vector3 lookat)
        {
            // Threat level None because this is what can already be done with the World Map in the viewer
            CheckThreatLevel(ThreatLevel.None, "osTeleportOwner");

            TeleportAgent(m_host.OwnerID.ToString(), regionName, position, lookat, true);
        }

        public void osTeleportOwner(LSL_Types.Vector3 position, LSL_Types.Vector3 lookat)
        {
            osTeleportOwner(World.RegionInfo.RegionName, position, lookat);
        }

        public void osTeleportOwner(int regionX, int regionY, LSL_Types.Vector3 position, LSL_Types.Vector3 lookat)
        {
            CheckThreatLevel(ThreatLevel.None, "osTeleportOwner");

            TeleportAgent(m_host.OwnerID.ToString(), regionX, regionY, position, lookat, true);
        }

        // Functions that get information from the agent itself.
        //
        // osGetAgentIP - this is used to determine the IP address of
        //the client.  This is needed to help configure other in world
        //resources based on the IP address of the clients connected.
        //I think High is a good risk level for this, as it is an
        //information leak.
        public string osGetAgentIP(string agent)
        {
            CheckThreatLevel(ThreatLevel.High, "osGetAgentIP");

            UUID avatarID = (UUID)agent;

            m_host.AddScriptLPS(1);
            if (World.Entities.ContainsKey((UUID)agent) && World.Entities[avatarID] is ScenePresence)
            {
                ScenePresence target = (ScenePresence)World.Entities[avatarID];
                return target.ControllingClient.RemoteEndPoint.Address.ToString();
            }
            
            // fall through case, just return nothing
            return "";
        }

        // Get a list of all the avatars/agents in the region
        public LSL_List osGetAgents()
        {
            // threat level is None as we could get this information with an
            // in-world script as well, just not as efficient
            CheckThreatLevel(ThreatLevel.None, "osGetAgents");
            m_host.AddScriptLPS(1);

            LSL_List result = new LSL_List();
            World.ForEachRootScenePresence(delegate(ScenePresence sp)
            {
                result.Add(new LSL_String(sp.Name));
            });
            return result;
        }

        // Adam's super super custom animation functions
        public void osAvatarPlayAnimation(string avatar, string animation)
        {
            CheckThreatLevel(ThreatLevel.VeryHigh, "osAvatarPlayAnimation");

            AvatarPlayAnimation(avatar, animation);
        }

        private void AvatarPlayAnimation(string avatar, string animation)
        {
            UUID avatarID = (UUID)avatar;

            m_host.AddScriptLPS(1);
            if (World.Entities.ContainsKey((UUID)avatar) && World.Entities[avatarID] is ScenePresence)
            {
                ScenePresence target = (ScenePresence)World.Entities[avatarID];
                if (target != null)
                {
                    UUID animID=UUID.Zero;
                    lock (m_host.TaskInventory)
                    {
                        foreach (KeyValuePair<UUID, TaskInventoryItem> inv in m_host.TaskInventory)
                        {
                            if (inv.Value.Name == animation)
                            {
                                if (inv.Value.Type == (int)AssetType.Animation)
                                    animID = inv.Value.AssetID;
                                continue;
                            }
                        }
                    }
                    if (animID == UUID.Zero)
                        target.Animator.AddAnimation(animation, m_host.UUID);
                    else
                        target.Animator.AddAnimation(animID, m_host.UUID);
                }
            }
        }

        public void osAvatarStopAnimation(string avatar, string animation)
        {
            CheckThreatLevel(ThreatLevel.VeryHigh, "osAvatarStopAnimation");

            AvatarStopAnimation(avatar, animation);
        }

        private void AvatarStopAnimation(string avatar, string animation)
        {
            UUID avatarID = (UUID)avatar;

            m_host.AddScriptLPS(1);

            // FIXME: What we really want to do here is factor out the similar code in llStopAnimation() to a common
            // method (though see that doesn't do the is animation check, which is probably a bug) and have both
            // these functions call that common code.  However, this does mean navigating the brain-dead requirement
            // of calling InitLSL()
            if (World.Entities.ContainsKey(avatarID) && World.Entities[avatarID] is ScenePresence)
            {
                ScenePresence target = (ScenePresence)World.Entities[avatarID];
                if (target != null)
                {
                    UUID animID;

                    if (!UUID.TryParse(animation, out animID))
                    {
                        TaskInventoryItem item = m_host.Inventory.GetInventoryItem(animation);
                        if (item != null && item.Type == (int)AssetType.Animation)
                            animID = item.AssetID;
                        else
                            animID = UUID.Zero;
                    }
                    
                    if (animID == UUID.Zero)
                        target.Animator.RemoveAnimation(animation);
                    else
                        target.Animator.RemoveAnimation(animID);
                }
            }
        }

        //Texture draw functions
        public string osMovePen(string drawList, int x, int y)
        {
            CheckThreatLevel(ThreatLevel.None, "osMovePen");

            m_host.AddScriptLPS(1);
            drawList += "MoveTo " + x + "," + y + ";";
            return drawList;
        }

        public string osDrawLine(string drawList, int startX, int startY, int endX, int endY)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawLine");

            m_host.AddScriptLPS(1);
            drawList += "MoveTo "+ startX+","+ startY +"; LineTo "+endX +","+endY +"; ";
            return drawList;
        }

        public string osDrawLine(string drawList, int endX, int endY)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawLine");

            m_host.AddScriptLPS(1);
            drawList += "LineTo " + endX + "," + endY + "; ";
            return drawList;
        }

        public string osDrawText(string drawList, string text)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawText");

            m_host.AddScriptLPS(1);
            drawList += "Text " + text + "; ";
            return drawList;
        }

        public string osDrawEllipse(string drawList, int width, int height)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawEllipse");

            m_host.AddScriptLPS(1);
            drawList += "Ellipse " + width + "," + height + "; ";
            return drawList;
        }

        public string osDrawRectangle(string drawList, int width, int height)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawRectangle");

            m_host.AddScriptLPS(1);
            drawList += "Rectangle " + width + "," + height + "; ";
            return drawList;
        }

        public string osDrawFilledRectangle(string drawList, int width, int height)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawFilledRectangle");

            m_host.AddScriptLPS(1);
            drawList += "FillRectangle " + width + "," + height + "; ";
            return drawList;
        }

        public string osDrawFilledPolygon(string drawList, LSL_List x, LSL_List y)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawFilledPolygon");

            m_host.AddScriptLPS(1);

            if (x.Length != y.Length || x.Length < 3)
            {
                return "";
            }
            drawList += "FillPolygon " + x.GetLSLStringItem(0) + "," + y.GetLSLStringItem(0);
            for (int i = 1; i < x.Length; i++)
            {
                drawList += "," + x.GetLSLStringItem(i) + "," + y.GetLSLStringItem(i);
            }
            drawList += "; ";
            return drawList;
        }

        public string osDrawPolygon(string drawList, LSL_List x, LSL_List y)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawPolygon");

            m_host.AddScriptLPS(1);

            if (x.Length != y.Length || x.Length < 3)
            {
                return "";
            }
            drawList += "Polygon " + x.GetLSLStringItem(0) + "," + y.GetLSLStringItem(0);
            for (int i = 1; i < x.Length; i++)
            {
                drawList += "," + x.GetLSLStringItem(i) + "," + y.GetLSLStringItem(i);
            }
            drawList += "; ";
            return drawList;
        }

        public string osSetFontSize(string drawList, int fontSize)
        {
            CheckThreatLevel(ThreatLevel.None, "osSetFontSize");

            m_host.AddScriptLPS(1);
            drawList += "FontSize "+ fontSize +"; ";
            return drawList;
        }

        public string osSetFontName(string drawList, string fontName)
        {
            CheckThreatLevel(ThreatLevel.None, "osSetFontName");

            m_host.AddScriptLPS(1);
            drawList += "FontName "+ fontName +"; ";
            return drawList;
        }

        public string osSetPenSize(string drawList, int penSize)
        {
            CheckThreatLevel(ThreatLevel.None, "osSetPenSize");

            m_host.AddScriptLPS(1);
            drawList += "PenSize " + penSize + "; ";
            return drawList;
        }

        public string osSetPenColor(string drawList, string color)
        {
            CheckThreatLevel(ThreatLevel.None, "osSetPenColor");
            
            m_host.AddScriptLPS(1);
            drawList += "PenColor " + color + "; ";
            return drawList;
        }

        // Deprecated
        public string osSetPenColour(string drawList, string colour)
        {
            CheckThreatLevel(ThreatLevel.None, "osSetPenColour");
            OSSLDeprecated("osSetPenColour", "osSetPenColor");

            m_host.AddScriptLPS(1);
            drawList += "PenColour " + colour + "; ";
            return drawList;
        }

        public string osSetPenCap(string drawList, string direction, string type)
        {
            CheckThreatLevel(ThreatLevel.None, "osSetPenCap");

            m_host.AddScriptLPS(1);
            drawList += "PenCap " + direction + "," + type + "; ";
            return drawList;
        }

        public string osDrawImage(string drawList, int width, int height, string imageUrl)
        {
            CheckThreatLevel(ThreatLevel.None, "osDrawImage");

            m_host.AddScriptLPS(1);
            drawList +="Image " +width + "," + height+ ","+ imageUrl +"; " ;
            return drawList;
        }

        public LSL_Vector osGetDrawStringSize(string contentType, string text, string fontName, int fontSize)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osGetDrawStringSize");
            m_host.AddScriptLPS(1);

            LSL_Vector vec = new LSL_Vector(0,0,0);
            IDynamicTextureManager textureManager = World.RequestModuleInterface<IDynamicTextureManager>();
            if (textureManager != null)
            {
                double xSize, ySize;
                textureManager.GetDrawStringSize(contentType, text, fontName, fontSize,
                                                 out xSize, out ySize);
                vec.x = xSize;
                vec.y = ySize;
            }
            return vec;
        }

        public void osSetStateEvents(int events)
        {
            // This function is a hack. There is no reason for it's existence
            // anymore, since state events now work properly.
            // It was probably added as a crutch or debugging aid, and
            // should be removed
            //
            CheckThreatLevel(ThreatLevel.High, "osSetStateEvents");
            m_host.AddScriptLPS(1);

            m_host.SetScriptEvents(m_item.ItemID, events);
        }

        public void osSetRegionWaterHeight(double height)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetRegionWaterHeight");

            m_host.AddScriptLPS(1);

            World.EventManager.TriggerRequestChangeWaterHeight((float)height);
        }

        /// <summary>
        /// Changes the Region Sun Settings, then Triggers a Sun Update
        /// </summary>
        /// <param name="useEstateSun">True to use Estate Sun instead of Region Sun</param>
        /// <param name="sunFixed">True to keep the sun stationary</param>
        /// <param name="sunHour">The "Sun Hour" that is desired, 0...24, with 0 just after SunRise</param>
        public void osSetRegionSunSettings(bool useEstateSun, bool sunFixed, double sunHour)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetRegionSunSettings");

            m_host.AddScriptLPS(1);

            while (sunHour > 24.0)
                sunHour -= 24.0;

            while (sunHour < 0)
                sunHour += 24.0;

            World.RegionInfo.RegionSettings.UseEstateSun = useEstateSun;
            World.RegionInfo.RegionSettings.SunPosition  = sunHour + 6; // LL Region Sun Hour is 6 to 30
            World.RegionInfo.RegionSettings.FixedSun     = sunFixed;
            World.RegionInfo.RegionSettings.Save();

            World.EventManager.TriggerEstateToolsSunUpdate(
                World.RegionInfo.RegionHandle, sunFixed, useEstateSun, (float)sunHour);
        }

        /// <summary>
        /// Changes the Estate Sun Settings, then Triggers a Sun Update
        /// </summary>
        /// <param name="sunFixed">True to keep the sun stationary, false to use global time</param>
        /// <param name="sunHour">The "Sun Hour" that is desired, 0...24, with 0 just after SunRise</param>
        public void osSetEstateSunSettings(bool sunFixed, double sunHour)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetEstateSunSettings");

            m_host.AddScriptLPS(1);

            while (sunHour > 24.0)
                sunHour -= 24.0;

            while (sunHour < 0)
                sunHour += 24.0;

            World.RegionInfo.EstateSettings.UseGlobalTime = !sunFixed;
            World.RegionInfo.EstateSettings.SunPosition = sunHour;
            World.RegionInfo.EstateSettings.FixedSun = sunFixed;
            World.RegionInfo.EstateSettings.Save();

            World.EventManager.TriggerEstateToolsSunUpdate(
                World.RegionInfo.RegionHandle, sunFixed, World.RegionInfo.RegionSettings.UseEstateSun, (float)sunHour);
        }

        /// <summary>
        /// Return the current Sun Hour 0...24, with 0 being roughly sun-rise
        /// </summary>
        /// <returns></returns>
        public double osGetCurrentSunHour()
        {
            CheckThreatLevel(ThreatLevel.None, "osGetCurrentSunHour");

            m_host.AddScriptLPS(1);

            // Must adjust for the fact that Region Sun Settings are still LL offset
            double sunHour = World.RegionInfo.RegionSettings.SunPosition - 6;

            // See if the sun module has registered itself, if so it's authoritative
            ISunModule module = World.RequestModuleInterface<ISunModule>();
            if (module != null)
            {
                sunHour = module.GetCurrentSunHour();
            }

            return sunHour;
        }

        public double osSunGetParam(string param)
        {
            CheckThreatLevel(ThreatLevel.None, "osSunGetParam");
            OSSLDeprecated("osSunGetParam", "osGetSunParam");
            return GetSunParam(param);
        }

        public double osGetSunParam(string param)
        {
            CheckThreatLevel(ThreatLevel.None, "osGetSunParam");
            return GetSunParam(param);
        }

        private double GetSunParam(string param)
        {
            m_host.AddScriptLPS(1);

            double value = 0.0;

            ISunModule module = World.RequestModuleInterface<ISunModule>();
            if (module != null)
            {
                value = module.GetSunParameter(param);
            }

            return value;
        }

        public void osSunSetParam(string param, double value)
        {
            CheckThreatLevel(ThreatLevel.None, "osSunSetParam");
            OSSLDeprecated("osSunSetParam", "osSetSunParam");
            SetSunParam(param, value);
        }

        public void osSetSunParam(string param, double value)
        {
            CheckThreatLevel(ThreatLevel.None, "osSetSunParam");
            SetSunParam(param, value);
        }

        private void SetSunParam(string param, double value)
        {
            m_host.AddScriptLPS(1);

            ISunModule module = World.RequestModuleInterface<ISunModule>();
            if (module != null)
            {
                module.SetSunParameter(param, value);
            }
        }

        public string osWindActiveModelPluginName()
        {
            CheckThreatLevel(ThreatLevel.None, "osWindActiveModelPluginName");
            m_host.AddScriptLPS(1);

            IWindModule module = World.RequestModuleInterface<IWindModule>();
            if (module != null)
            {
                return module.WindActiveModelPluginName;
            }

            return String.Empty;
        }

        public void osSetWindParam(string plugin, string param, LSL_Float value)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetWindParam");
            m_host.AddScriptLPS(1);

            IWindModule module = World.RequestModuleInterface<IWindModule>();
            if (module != null)
            {
                try
                {
                    module.WindParamSet(plugin, param, (float)value);
                }
                catch (Exception) { }
            }
        }

        public LSL_Float osGetWindParam(string plugin, string param)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osGetWindParam");
            m_host.AddScriptLPS(1);

            IWindModule module = World.RequestModuleInterface<IWindModule>();
            if (module != null)
            {
                return module.WindParamGet(plugin, param);
            }

            return 0.0f;
        }

        // Routines for creating and managing parcels programmatically
        public void osParcelJoin(LSL_Vector pos1, LSL_Vector pos2)
        {
            CheckThreatLevel(ThreatLevel.High, "osParcelJoin");
            m_host.AddScriptLPS(1);

            int startx = (int)(pos1.x < pos2.x ? pos1.x : pos2.x);
            int starty = (int)(pos1.y < pos2.y ? pos1.y : pos2.y);
            int endx = (int)(pos1.x > pos2.x ? pos1.x : pos2.x);
            int endy = (int)(pos1.y > pos2.y ? pos1.y : pos2.y);

            World.LandChannel.Join(startx,starty,endx,endy,m_host.OwnerID);
        }

        public void osParcelSubdivide(LSL_Vector pos1, LSL_Vector pos2)
        {
            CheckThreatLevel(ThreatLevel.High, "osParcelSubdivide");
            m_host.AddScriptLPS(1);

            int startx = (int)(pos1.x < pos2.x ? pos1.x : pos2.x);
            int starty = (int)(pos1.y < pos2.y ? pos1.y : pos2.y);
            int endx = (int)(pos1.x > pos2.x ? pos1.x : pos2.x);
            int endy = (int)(pos1.y > pos2.y ? pos1.y : pos2.y);

            World.LandChannel.Subdivide(startx,starty,endx,endy,m_host.OwnerID);
        }

        public void osParcelSetDetails(LSL_Vector pos, LSL_List rules)
        {
            const string functionName = "osParcelSetDetails";
            CheckThreatLevel(ThreatLevel.High, functionName);
            OSSLDeprecated(functionName, "osSetParcelDetails");
            SetParcelDetails(pos, rules, functionName);
        }

        public void osSetParcelDetails(LSL_Vector pos, LSL_List rules)
        {
            const string functionName = "osSetParcelDetails";
            CheckThreatLevel(ThreatLevel.High, functionName);
            SetParcelDetails(pos, rules, functionName);
        }

        private void SetParcelDetails(LSL_Vector pos, LSL_List rules, string functionName)
        {
            m_host.AddScriptLPS(1);

            // Get a reference to the land data and make sure the owner of the script
            // can modify it

            ILandObject startLandObject = World.LandChannel.GetLandObject((int)pos.x, (int)pos.y);
            if (startLandObject == null)
            {
                OSSLShoutError("There is no land at that location");
                return;
            }

            if (!World.Permissions.CanEditParcelProperties(m_host.OwnerID, startLandObject, GroupPowers.LandOptions))
            {
                OSSLShoutError("You do not have permission to modify the parcel");
                return;
            }

            // Create a new land data object we can modify
            LandData newLand = startLandObject.LandData.Copy();
            UUID uuid;

            // Process the rules, not sure what the impact would be of changing owner or group
            for (int idx = 0; idx < rules.Length;)
            {
                int code = rules.GetLSLIntegerItem(idx++);
                string arg = rules.GetLSLStringItem(idx++);
                switch (code)
                {
                    case ScriptBaseClass.PARCEL_DETAILS_NAME:
                        newLand.Name = arg;
                        break;

                    case ScriptBaseClass.PARCEL_DETAILS_DESC:
                        newLand.Description = arg;
                        break;

                    case ScriptBaseClass.PARCEL_DETAILS_OWNER:
                        CheckThreatLevel(ThreatLevel.VeryHigh, functionName);
                        if (UUID.TryParse(arg, out uuid))
                            newLand.OwnerID = uuid;
                        break;

                    case ScriptBaseClass.PARCEL_DETAILS_GROUP:
                        CheckThreatLevel(ThreatLevel.VeryHigh, functionName);
                        if (UUID.TryParse(arg, out uuid))
                            newLand.GroupID = uuid;
                        break;

                    case ScriptBaseClass.PARCEL_DETAILS_CLAIMDATE:
                        CheckThreatLevel(ThreatLevel.VeryHigh, functionName);
                        newLand.ClaimDate = Convert.ToInt32(arg);
                        if (newLand.ClaimDate == 0)
                            newLand.ClaimDate = Util.UnixTimeSinceEpoch();
                        break;
                 }
             }

            World.LandChannel.UpdateLandObject(newLand.LocalID,newLand);
        }

        public double osList2Double(LSL_Types.list src, int index)
        {
            // There is really no double type in OSSL. C# and other
            // have one, but the current implementation of LSL_Types.list
            // is not allowed to contain any.
            // This really should be removed.
            //
            CheckThreatLevel(ThreatLevel.None, "osList2Double");

            m_host.AddScriptLPS(1);
            if (index < 0)
            {
                index = src.Length + index;
            }
            if (index >= src.Length)
            {
                return 0.0;
            }
            return Convert.ToDouble(src.Data[index]);
        }

        public void osSetParcelMediaURL(string url)
        {
            // What actually is the difference to the LL function?
            //
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetParcelMediaURL");

            m_host.AddScriptLPS(1);

            ILandObject land
                = World.LandChannel.GetLandObject(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y);

            if (land.LandData.OwnerID != m_host.OwnerID)
                return;

            land.SetMediaUrl(url);
        }

        public void osSetParcelSIPAddress(string SIPAddress)
        {
            // What actually is the difference to the LL function?
            //
            CheckThreatLevel(ThreatLevel.VeryLow, "osSetParcelSIPAddress");

            m_host.AddScriptLPS(1);

            ILandObject land
                = World.LandChannel.GetLandObject(m_host.AbsolutePosition.X, m_host.AbsolutePosition.Y);

            if (land.LandData.OwnerID != m_host.OwnerID)
            {
                OSSLError("osSetParcelSIPAddress: Sorry, you need to own the land to use this function");
                return;
            }

            // get the voice module
            IVoiceModule voiceModule = World.RequestModuleInterface<IVoiceModule>();

            if (voiceModule != null)
                voiceModule.setLandSIPAddress(SIPAddress,land.LandData.GlobalID);
            else
                OSSLError("osSetParcelSIPAddress: No voice module enabled for this land");
        }

        public string osGetScriptEngineName()
        {
            // This gets a "high" because knowing the engine may be used
            // to exploit engine-specific bugs or induce usage patterns
            // that trigger engine-specific failures.
            // Besides, public grid users aren't supposed to know.
            //
            CheckThreatLevel(ThreatLevel.High, "osGetScriptEngineName");

            m_host.AddScriptLPS(1);

            int scriptEngineNameIndex = 0;

            if (!String.IsNullOrEmpty(m_ScriptEngine.ScriptEngineName))
            {
                // parse off the "ScriptEngine."
                scriptEngineNameIndex = m_ScriptEngine.ScriptEngineName.IndexOf(".", scriptEngineNameIndex);
                scriptEngineNameIndex++; // get past delimiter

                int scriptEngineNameLength = m_ScriptEngine.ScriptEngineName.Length - scriptEngineNameIndex;

                // create char array then a string that is only the script engine name
                Char[] scriptEngineNameCharArray = m_ScriptEngine.ScriptEngineName.ToCharArray(scriptEngineNameIndex, scriptEngineNameLength);
                String scriptEngineName = new String(scriptEngineNameCharArray);

                return scriptEngineName;
            }
            else
            {
                return String.Empty;
            }
        }

        public string osGetSimulatorVersion()
        {
            // High because it can be used to target attacks to known weaknesses
            // This would allow a new class of griefer scripts that don't even
            // require their user to know what they are doing (see script
            // kiddie)
            //
            CheckThreatLevel(ThreatLevel.High,"osGetSimulatorVersion");
            m_host.AddScriptLPS(1);

            return m_ScriptEngine.World.GetSimulatorVersion();
        }

        private Hashtable osdToHashtable(OSDMap map)
        {
            Hashtable result = new Hashtable();
            foreach (KeyValuePair<string, OSD> item in map) {
                result.Add(item.Key, osdToObject(item.Value));
            }
            return result;
        }
        
        private ArrayList osdToArray(OSDArray list)
        {
            ArrayList result = new ArrayList();
            foreach ( OSD item in list ) {
                result.Add(osdToObject(item));
            }
            return result;
        }

        private Object osdToObject(OSD decoded)
        {
            if ( decoded is OSDString ) {
                return (string) decoded.AsString();
            } else if ( decoded is OSDInteger ) {
                return (int) decoded.AsInteger();
            } else if ( decoded is OSDReal ) {
                return (float) decoded.AsReal();
            } else if ( decoded is OSDBoolean ) {
                return (bool) decoded.AsBoolean();
            } else if ( decoded is OSDMap ) {
                return osdToHashtable((OSDMap) decoded);
            } else if ( decoded is OSDArray ) {
                return osdToArray((OSDArray) decoded);
            } else {
                return null;
            }
        }

        public Object osParseJSONNew(string JSON)
        {
            CheckThreatLevel(ThreatLevel.None, "osParseJSONNew");

            m_host.AddScriptLPS(1);

            try
            {
                OSD decoded = OSDParser.DeserializeJson(JSON);
                return osdToObject(decoded);
            }
            catch(Exception e)
            {
                OSSLError("osParseJSONNew: Problems decoding JSON string " + JSON + " : " + e.Message) ;
                return null;
            }
        }

        public Hashtable osParseJSON(string JSON)
        {
            CheckThreatLevel(ThreatLevel.None, "osParseJSON");

            m_host.AddScriptLPS(1);

            Object decoded = osParseJSONNew(JSON);
            
            if ( decoded is Hashtable ) {
                return (Hashtable) decoded;
            } else if ( decoded is ArrayList ) {
                ArrayList decoded_list = (ArrayList) decoded;
                Hashtable fakearray = new Hashtable();
                int i = 0;
                for ( i  = 0; i < decoded_list.Count ; i++ ) {
                        fakearray.Add(i, decoded_list[i]);
                }
                return fakearray;
            } else {
                OSSLError("osParseJSON: unable to parse JSON string " + JSON);
                return null;
            }
        }

        /// <summary>
        /// Send a message to to object identified by the given UUID
        /// </summary>
        /// <remarks>
        /// A script in the object must implement the dataserver function
        /// the dataserver function is passed the ID of the calling function and a string message
        /// </remarks>
        /// <param name="objectUUID"></param>
        /// <param name="message"></param>
        public void osMessageObject(LSL_Key objectUUID, string message)
        {
            CheckThreatLevel(ThreatLevel.Low, "osMessageObject");
            m_host.AddScriptLPS(1);

            UUID objUUID;
            if (!UUID.TryParse(objectUUID, out objUUID)) // prior to patching, a thrown exception regarding invalid GUID format would be shouted instead.
            {
                OSSLShoutError("osMessageObject() cannot send messages to objects with invalid UUIDs");
                return;
            }

            MessageObject(objUUID, message);
        }

        private void MessageObject(UUID objUUID, string message)
        {
            object[] resobj = new object[] { new LSL_Types.LSLString(m_host.UUID.ToString()), new LSL_Types.LSLString(message) };

            SceneObjectPart sceneOP = World.GetSceneObjectPart(objUUID);

            if (sceneOP == null) // prior to patching, PostObjectEvent() would cause a throw exception to be shouted instead.
            {
                OSSLShoutError("osMessageObject() cannot send message to " + objUUID.ToString() + ", object was not found in scene.");
                return;
            }

            m_ScriptEngine.PostObjectEvent(
                sceneOP.LocalId, new EventParams(
                    "dataserver", resobj, new DetectParams[0]));
        }

        /// <summary>
        /// Write a notecard directly to the prim's inventory.
        /// </summary>
        /// <remarks>
        /// This needs ThreatLevel high. It is an excellent griefer tool,
        /// In a loop, it can cause asset bloat and DOS levels of asset
        /// writes.
        /// </remarks>
        /// <param name="notecardName">The name of the notecard to write.</param>
        /// <param name="contents">The contents of the notecard.</param>
        public void osMakeNotecard(string notecardName, LSL_Types.list contents)
        {
            CheckThreatLevel(ThreatLevel.High, "osMakeNotecard");
            m_host.AddScriptLPS(1);

            StringBuilder notecardData = new StringBuilder();

            for (int i = 0; i < contents.Length; i++)
                notecardData.Append((string)(contents.GetLSLStringItem(i) + "\n"));

            SaveNotecard(notecardName, "Script generated notecard", notecardData.ToString(), false);
        }

        /// <summary>
        /// Save a notecard to prim inventory.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="description">Description of notecard</param>
        /// <param name="notecardData"></param>
        /// <param name="forceSameName">
        /// If true, then if an item exists with the same name, it is replaced.
        /// If false, then a new item is created witha slightly different name (e.g. name 1)
        /// </param>
        /// <returns>Prim inventory item created.</returns>
        protected TaskInventoryItem SaveNotecard(string name, string description, string data, bool forceSameName)
        {
            // Create new asset
            AssetBase asset = new AssetBase(UUID.Random(), name, (sbyte)AssetType.Notecard, m_host.OwnerID.ToString());
            asset.Description = description;

            int textLength = data.Length;
            data
                = "Linden text version 2\n{\nLLEmbeddedItems version 1\n{\ncount 0\n}\nText length "
                    + textLength.ToString() + "\n" + data + "}\n";

            asset.Data = Util.UTF8.GetBytes(data);
            World.AssetService.Store(asset);

            // Create Task Entry
            TaskInventoryItem taskItem = new TaskInventoryItem();

            taskItem.ResetIDs(m_host.UUID);
            taskItem.ParentID = m_host.UUID;
            taskItem.CreationDate = (uint)Util.UnixTimeSinceEpoch();
            taskItem.Name = asset.Name;
            taskItem.Description = asset.Description;
            taskItem.Type = (int)AssetType.Notecard;
            taskItem.InvType = (int)InventoryType.Notecard;
            taskItem.OwnerID = m_host.OwnerID;
            taskItem.CreatorID = m_host.OwnerID;
            taskItem.BasePermissions = (uint)PermissionMask.All;
            taskItem.CurrentPermissions = (uint)PermissionMask.All;
            taskItem.EveryonePermissions = 0;
            taskItem.NextPermissions = (uint)PermissionMask.All;
            taskItem.GroupID = m_host.GroupID;
            taskItem.GroupPermissions = 0;
            taskItem.Flags = 0;
            taskItem.PermsGranter = UUID.Zero;
            taskItem.PermsMask = 0;
            taskItem.AssetID = asset.FullID;

            if (forceSameName)
                m_host.Inventory.AddInventoryItemExclusive(taskItem, false);
            else
                m_host.Inventory.AddInventoryItem(taskItem, false);

            return taskItem;
        }

        /// <summary>
        /// Load the notecard data found at the given prim inventory item name or asset uuid.
        /// </summary>
        /// <param name="notecardNameOrUuid"></param>
        /// <returns>The text loaded.  Null if no notecard was found.</returns>
        protected string LoadNotecard(string notecardNameOrUuid)
        {
            UUID assetID = CacheNotecard(notecardNameOrUuid);
            StringBuilder notecardData = new StringBuilder();

            for (int count = 0; count < NotecardCache.GetLines(assetID); count++)
            {
                string line = NotecardCache.GetLine(assetID, count) + "\n";

//                m_log.DebugFormat("[OSSL]: From notecard {0} loading line {1}", notecardNameOrUuid, line);

                notecardData.Append(line);
            }

            return notecardData.ToString();
        }

        /// <summary>
        /// Cache a notecard's contents.
        /// </summary>
        /// <param name="notecardNameOrUuid"></param>
        /// <returns>
        /// The asset id of the notecard, which is used for retrieving the cached data.
        /// UUID.Zero if no asset could be found.
        /// </returns>
        protected UUID CacheNotecard(string notecardNameOrUuid)
        {
            UUID assetID = UUID.Zero;

            if (!UUID.TryParse(notecardNameOrUuid, out assetID))
            {
                foreach (TaskInventoryItem item in m_host.TaskInventory.Values)
                {
                    if (item.Type == 7 && item.Name == notecardNameOrUuid)
                    {
                        assetID = item.AssetID;
                    }
                }
            }

            if (assetID == UUID.Zero)
                return UUID.Zero;

            if (!NotecardCache.IsCached(assetID))
            {
                AssetBase a = World.AssetService.Get(assetID.ToString());

                if (a == null)
                    return UUID.Zero;

                string data = Encoding.UTF8.GetString(a.Data);
                NotecardCache.Cache(assetID, data);
            };

            return assetID;
        }

        /// <summary>
        /// Directly get an entire notecard at once.
        /// </summary>
        /// <remarks>
        /// Instead of using the LSL Dataserver event to pull notecard data
        /// this will simply read the entire notecard and return its data as a string.
        ///
        /// Warning - due to the synchronous method this function uses to fetch assets, its use
        ///            may be dangerous and unreliable while running in grid mode.
        /// </remarks>
        /// <param name="name">Name of the notecard or its asset id</param>
        /// <param name="line">The line number to read.  The first line is line 0</param>
        /// <returns>Notecard line</returns>
        public string osGetNotecardLine(string name, int line)
        {
            CheckThreatLevel(ThreatLevel.VeryHigh, "osGetNotecardLine");
            m_host.AddScriptLPS(1);

            UUID assetID = CacheNotecard(name);

            if (assetID == UUID.Zero)
            {
                OSSLShoutError("Notecard '" + name + "' could not be found.");
                return "ERROR!";
            }

            return NotecardCache.GetLine(assetID, line);
        }

        /// <summary>
        /// Get an entire notecard at once.
        /// </summary>
        /// <remarks>
        /// Instead of using the LSL Dataserver event to pull notecard data line by line,
        /// this will simply read the entire notecard and return its data as a string.
        ///
        /// Warning - due to the synchronous method this function uses to fetch assets, its use
        ///            may be dangerous and unreliable while running in grid mode.
        /// </remarks>
        /// <param name="name">Name of the notecard or its asset id</param>
        /// <returns>Notecard text</returns>
        public string osGetNotecard(string name)
        {
            CheckThreatLevel(ThreatLevel.VeryHigh, "osGetNotecard");
            m_host.AddScriptLPS(1);

            string text = LoadNotecard(name);

            if (text == null)
            {
                OSSLShoutError("Notecard '" + name + "' could not be found.");
                return "ERROR!";
            }
            else
            {
                return text;
            }
        }

        /// <summary>
        /// Get the number of lines in the given notecard.
        /// </summary>
        /// <remarks>
        /// Instead of using the LSL Dataserver event to pull notecard data,
        /// this will simply read the number of note card lines and return this data as an integer.
        ///
        /// Warning - due to the synchronous method this function uses to fetch assets, its use
        ///            may be dangerous and unreliable while running in grid mode.
        /// </remarks>
        /// <param name="name">Name of the notecard or its asset id</param>
        /// <returns></returns>
        public int osGetNumberOfNotecardLines(string name)
        {
            CheckThreatLevel(ThreatLevel.VeryHigh, "osGetNumberOfNotecardLines");
            m_host.AddScriptLPS(1);

            UUID assetID = CacheNotecard(name);

            if (assetID == UUID.Zero)
            {
                OSSLShoutError("Notecard '" + name + "' could not be found.");
                return -1;
            }

            return NotecardCache.GetLines(assetID);
        }

        public string osAvatarName2Key(string firstname, string lastname)
        {
            CheckThreatLevel(ThreatLevel.Low, "osAvatarName2Key");
            m_host.AddScriptLPS(1);

            UserAccount account = World.UserAccountService.GetUserAccount(World.RegionInfo.ScopeID, firstname, lastname);
            if (null == account)
            {
                return UUID.Zero.ToString();
            }
            else
            {
                return account.PrincipalID.ToString();
            }
        }

        public string osKey2Name(string id)
        {
            CheckThreatLevel(ThreatLevel.Low, "osKey2Name");
            m_host.AddScriptLPS(1);

            UUID key = new UUID();

            if (UUID.TryParse(id, out key))
            {
                UserAccount account = World.UserAccountService.GetUserAccount(World.RegionInfo.ScopeID, key);
                if (null == account)
                {
                    return "";
                }
                else
                {
                    return account.Name;
                }
            }
            else
            {
                return "";
            }
        }

        private enum InfoType
        {
            Nick,
            Name,
            Login,
            Home,
            Custom
        };

        private string GridUserInfo(InfoType type)
        {
            return GridUserInfo(type, "");
        }

        private string GridUserInfo(InfoType type, string key)
        {
            string retval = String.Empty;
            IConfigSource config = m_ScriptEngine.ConfigSource;
            string url = null;

            IConfig gridInfoConfig = config.Configs["GridInfo"];

            if (gridInfoConfig != null)
                url = gridInfoConfig.GetString("GridInfoURI", String.Empty);

            if (String.IsNullOrEmpty(url))
                return "Configuration Error!";

            string verb ="/json_grid_info";
            OSDMap json = new OSDMap();

            OSDMap info =  WebUtil.GetFromService(String.Format("{0}{1}",url,verb), 3000);

            if (info["Success"] != true)
                return "Get GridInfo Failed!";

            json = (OSDMap)OSDParser.DeserializeJson(info["_RawResult"].AsString());

            switch (type)
            {
                case InfoType.Nick:
                    retval = json["gridnick"];
                    break;

                case InfoType.Name:
                    retval = json["gridname"];
                    break;

                case InfoType.Login:
                    retval = json["login"];
                    break;

                case InfoType.Home:
                    retval = json["home"];
                    break;

                case InfoType.Custom:
                    retval = json[key];
                    break;

                default:
                    retval = "error";
                    break;
            }

            return retval;
        }

        /// <summary>
        /// Get the nickname of this grid, as set in the [GridInfo] config section.
        /// </summary>
        /// <remarks>
        /// Threat level is Moderate because intentional abuse, for instance
        /// scripts that are written to be malicious only on one grid,
        /// for instance in a HG scenario, are a distinct possibility.
        /// </remarks>
        /// <returns></returns>
        public string osGetGridNick()
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetGridNick");
            m_host.AddScriptLPS(1);

            string nick = String.Empty;
            IConfigSource config = m_ScriptEngine.ConfigSource;

            if (config.Configs[GridInfoServiceConfigSectionName] != null)
                nick = config.Configs[GridInfoServiceConfigSectionName].GetString("gridnick", nick);

            if (String.IsNullOrEmpty(nick))
                nick = GridUserInfo(InfoType.Nick);

            return nick;
        }

        public string osGetGridName()
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetGridName");
            m_host.AddScriptLPS(1);

            string name = String.Empty;
            IConfigSource config = m_ScriptEngine.ConfigSource;

            if (config.Configs[GridInfoServiceConfigSectionName] != null)
                name = config.Configs[GridInfoServiceConfigSectionName].GetString("gridname", name);

            if (String.IsNullOrEmpty(name))
                name = GridUserInfo(InfoType.Name);

            return name;
        }

        public string osGetGridLoginURI()
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetGridLoginURI");
            m_host.AddScriptLPS(1);

            string loginURI = String.Empty;
            IConfigSource config = m_ScriptEngine.ConfigSource;

            if (config.Configs[GridInfoServiceConfigSectionName] != null)
                loginURI = config.Configs[GridInfoServiceConfigSectionName].GetString("login", loginURI);

            if (String.IsNullOrEmpty(loginURI))
                loginURI = GridUserInfo(InfoType.Login);

            return loginURI;
        }

        public string osGetGridHomeURI()
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetGridHomeURI");
            m_host.AddScriptLPS(1);

            string HomeURI = String.Empty;
            IConfigSource config = m_ScriptEngine.ConfigSource;

            if (config.Configs["LoginService"] != null)
                HomeURI = config.Configs["LoginService"].GetString("SRV_HomeURI", HomeURI);

            if (String.IsNullOrEmpty(HomeURI))
                HomeURI = GridUserInfo(InfoType.Home);

            return HomeURI;
        }

        public string osGetGridGatekeeperURI()
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetGridGatekeeperURI");
            m_host.AddScriptLPS(1);

            string gatekeeperURI = String.Empty;
            IConfigSource config = m_ScriptEngine.ConfigSource;

            if (config.Configs["GridService"] != null)
                gatekeeperURI = config.Configs["GridService"].GetString("Gatekeeper", gatekeeperURI);

            return gatekeeperURI;
        }

        public string osGetGridCustom(string key)
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetGridCustom");
            m_host.AddScriptLPS(1);

            string retval = String.Empty;
            IConfigSource config = m_ScriptEngine.ConfigSource;

            if (config.Configs[GridInfoServiceConfigSectionName] != null)
                retval = config.Configs[GridInfoServiceConfigSectionName].GetString(key, retval);

            if (String.IsNullOrEmpty(retval))
                retval = GridUserInfo(InfoType.Custom, key);

            return retval;
        }

        public LSL_String osFormatString(string str, LSL_List strings)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osFormatString");
            m_host.AddScriptLPS(1);

            return String.Format(str, strings.Data);
        }

        public LSL_List osMatchString(string src, string pattern, int start)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osMatchString");
            m_host.AddScriptLPS(1);

            LSL_List result = new LSL_List();

            // Normalize indices (if negative).
            // After normlaization they may still be
            // negative, but that is now relative to
            // the start, rather than the end, of the
            // sequence.
            if (start < 0)
            {
                start = src.Length + start;
            }

            if (start < 0 || start >= src.Length)
            {
                return result;  // empty list
            }

            // Find matches beginning at start position
            Regex matcher = new Regex(pattern);
            Match match = matcher.Match(src, start);
            while (match.Success)
            {
                foreach (System.Text.RegularExpressions.Group g in match.Groups)
                {
                    if (g.Success)
                    {
                        result.Add(new LSL_String(g.Value));
                        result.Add(new LSL_Integer(g.Index));
                    }
                }

                match = match.NextMatch();
            }

            return result;
        }

        public LSL_String osReplaceString(string src, string pattern, string replace, int count, int start)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osReplaceString");
            m_host.AddScriptLPS(1);

            // Normalize indices (if negative).
            // After normlaization they may still be
            // negative, but that is now relative to
            // the start, rather than the end, of the
            // sequence.
            if (start < 0)
            {
                start = src.Length + start;
            }

            if (start < 0 || start >= src.Length)
            {
                return src;
            }

            // Find matches beginning at start position
            Regex matcher = new Regex(pattern);
            return matcher.Replace(src,replace,count,start);
        }

        public string osLoadedCreationDate()
        {
            CheckThreatLevel(ThreatLevel.Low, "osLoadedCreationDate");
            m_host.AddScriptLPS(1);

            return World.RegionInfo.RegionSettings.LoadedCreationDate;
        }

        public string osLoadedCreationTime()
        {
            CheckThreatLevel(ThreatLevel.Low, "osLoadedCreationTime");
            m_host.AddScriptLPS(1);

            return World.RegionInfo.RegionSettings.LoadedCreationTime;
        }

        public string osLoadedCreationID()
        {
            CheckThreatLevel(ThreatLevel.Low, "osLoadedCreationID");
            m_host.AddScriptLPS(1);

            return World.RegionInfo.RegionSettings.LoadedCreationID;
        }

        /// <summary>
        /// Get the primitive parameters of a linked prim.
        /// </summary>
        /// <remarks>
        /// Threat level is 'Low' because certain users could possibly be tricked into
        /// dropping an unverified script into one of their own objects, which could
        /// then gather the physical construction details of the object and transmit it
        /// to an unscrupulous third party, thus permitting unauthorized duplication of
        /// the object's form.
        /// </remarks>
        /// <param name="linknumber"></param>
        /// <param name="rules"></param>
        /// <returns></returns>
        public LSL_List osGetLinkPrimitiveParams(int linknumber, LSL_List rules)
        {
            CheckThreatLevel(ThreatLevel.High, "osGetLinkPrimitiveParams");
            m_host.AddScriptLPS(1);
            InitLSL();
            // One needs to cast m_LSL_Api because we're using functions not
            // on the ILSL_Api interface.
            LSL_Api LSL_Api = (LSL_Api)m_LSL_Api;
            LSL_List retVal = new LSL_List();
            LSL_List remaining = null;
            List<SceneObjectPart> parts = LSL_Api.GetLinkParts(linknumber);
            foreach (SceneObjectPart part in parts)
            {
                remaining = LSL_Api.GetPrimParams(part, rules, ref retVal);
            }

            while (remaining != null && remaining.Length > 2)
            {
                linknumber = remaining.GetLSLIntegerItem(0);
                rules = remaining.GetSublist(1, -1);
                parts = LSL_Api.GetLinkParts(linknumber);

                foreach (SceneObjectPart part in parts)
                    remaining = LSL_Api.GetPrimParams(part, rules, ref retVal);
            }
            return retVal;
        }

        public LSL_Integer osIsNpc(LSL_Key npc)
        {
            CheckThreatLevel(ThreatLevel.None, "osIsNpc");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId;
                if (UUID.TryParse(npc.m_string, out npcId))
                    if (module.IsNPC(npcId, World))
                        return ScriptBaseClass.TRUE;
            }

            return ScriptBaseClass.FALSE;
        }

        public LSL_Key osNpcCreate(string firstname, string lastname, LSL_Vector position, string notecard)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcCreate");
            m_host.AddScriptLPS(1);

            return NpcCreate(firstname, lastname, position, notecard, false, false);
        }

        public LSL_Key osNpcCreate(string firstname, string lastname, LSL_Vector position, string notecard, int options)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcCreate");
            m_host.AddScriptLPS(1);

            return NpcCreate(
                firstname, lastname, position, notecard,
                (options & ScriptBaseClass.OS_NPC_NOT_OWNED) == 0,
                (options & ScriptBaseClass.OS_NPC_SENSE_AS_AGENT) != 0);
        }

        private LSL_Key NpcCreate(
            string firstname, string lastname, LSL_Vector position, string notecard, bool owned, bool senseAsAgent)
        {
            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                AvatarAppearance appearance = null;

                UUID id;
                if (UUID.TryParse(notecard, out id))
                {
                    ScenePresence clonePresence = World.GetScenePresence(id);
                    if (clonePresence != null)
                        appearance = clonePresence.Appearance;
                }

                if (appearance == null)
                {
                    string appearanceSerialized = LoadNotecard(notecard);

                    if (appearanceSerialized != null)
                    {
                        OSDMap appearanceOsd = (OSDMap)OSDParser.DeserializeLLSDXml(appearanceSerialized);
                        appearance = new AvatarAppearance();
                        appearance.Unpack(appearanceOsd);
                    }
                }

                if (appearance == null)
                    return new LSL_Key(UUID.Zero.ToString());

                UUID ownerID = UUID.Zero;
                if (owned)
                    ownerID = m_host.OwnerID;
                UUID x = module.CreateNPC(firstname,
                                          lastname,
                                          position,
                                          ownerID,
                                          senseAsAgent,
                                          World,
                                          appearance);

                return new LSL_Key(x.ToString());
            }

            return new LSL_Key(UUID.Zero.ToString());
        }

        /// <summary>
        /// Save the current appearance of the NPC permanently to the named notecard.
        /// </summary>
        /// <param name="avatar"></param>
        /// <param name="notecard">The name of the notecard to which to save the appearance.</param>
        /// <returns>The asset ID of the notecard saved.</returns>
        public LSL_Key osNpcSaveAppearance(LSL_Key npc, string notecard)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcSaveAppearance");
            m_host.AddScriptLPS(1);

            INPCModule npcModule = World.RequestModuleInterface<INPCModule>();

            if (npcModule != null)
            {
                UUID npcId;
                if (!UUID.TryParse(npc.m_string, out npcId))
                    return new LSL_Key(UUID.Zero.ToString());

                if (!npcModule.CheckPermissions(npcId, m_host.OwnerID))
                    return new LSL_Key(UUID.Zero.ToString());

                return SaveAppearanceToNotecard(npcId, notecard);
            }

            return new LSL_Key(UUID.Zero.ToString());
        }

        public void osNpcLoadAppearance(LSL_Key npc, string notecard)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcLoadAppearance");
            m_host.AddScriptLPS(1);

            INPCModule npcModule = World.RequestModuleInterface<INPCModule>();

            if (npcModule != null)
            {
                UUID npcId;
                if (!UUID.TryParse(npc.m_string, out npcId))
                    return;

                if (!npcModule.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                string appearanceSerialized = LoadNotecard(notecard);
                OSDMap appearanceOsd = (OSDMap)OSDParser.DeserializeLLSDXml(appearanceSerialized);
//                OSD a = OSDParser.DeserializeLLSDXml(appearanceSerialized);
//                Console.WriteLine("appearanceSerialized {0}", appearanceSerialized);
//                Console.WriteLine("a.Type {0}, a.ToString() {1}", a.Type, a);
                AvatarAppearance appearance = new AvatarAppearance();
                appearance.Unpack(appearanceOsd);

                npcModule.SetNPCAppearance(npcId, appearance, m_host.ParentGroup.Scene);
            }
        }

        public LSL_Key osNpcGetOwner(LSL_Key npc)
        {
            CheckThreatLevel(ThreatLevel.None, "osNpcGetOwner");
            m_host.AddScriptLPS(1);

            INPCModule npcModule = World.RequestModuleInterface<INPCModule>();
            if (npcModule != null)
            {
                UUID npcId;
                if (UUID.TryParse(npc.m_string, out npcId))
                {
                    UUID owner = npcModule.GetOwner(npcId);
                    if (owner != UUID.Zero)
                        return new LSL_Key(owner.ToString());
                    else
                        return npc;
                }
            }

            return new LSL_Key(UUID.Zero.ToString());
        }

        public LSL_Vector osNpcGetPos(LSL_Key npc)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcGetPos");
            m_host.AddScriptLPS(1);

            INPCModule npcModule = World.RequestModuleInterface<INPCModule>();
            if (npcModule != null)
            {
                UUID npcId;
                if (!UUID.TryParse(npc.m_string, out npcId))
                    return new LSL_Vector(0, 0, 0);

                if (!npcModule.CheckPermissions(npcId, m_host.OwnerID))
                    return new LSL_Vector(0, 0, 0);

                ScenePresence sp = World.GetScenePresence(npcId);

                if (sp != null)
                {
                    Vector3 pos = sp.AbsolutePosition;
                    return new LSL_Vector(pos.X, pos.Y, pos.Z);
                }
            }

            return new LSL_Vector(0, 0, 0);
        }

        public void osNpcMoveTo(LSL_Key npc, LSL_Vector pos)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcMoveTo");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId;
                if (!UUID.TryParse(npc.m_string, out npcId))
                    return;

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;
                
                module.MoveToTarget(npcId, World, pos, false, true, false);
            }
        }

        public void osNpcMoveToTarget(LSL_Key npc, LSL_Vector target, int options)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcMoveToTarget");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId;
                if (!UUID.TryParse(npc.m_string, out npcId))
                    return;

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.MoveToTarget(
                    new UUID(npc.m_string),
                    World,
                    target,
                    (options & ScriptBaseClass.OS_NPC_NO_FLY) != 0,
                    (options & ScriptBaseClass.OS_NPC_LAND_AT_TARGET) != 0,
                    (options & ScriptBaseClass.OS_NPC_RUNNING) != 0);
            }
        }

        public LSL_Rotation osNpcGetRot(LSL_Key npc)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcGetRot");
            m_host.AddScriptLPS(1);

            INPCModule npcModule = World.RequestModuleInterface<INPCModule>();
            if (npcModule != null)
            {
                UUID npcId;
                if (!UUID.TryParse(npc.m_string, out npcId))
                    return new LSL_Rotation(Quaternion.Identity.X, Quaternion.Identity.Y, Quaternion.Identity.Z, Quaternion.Identity.W);

                if (!npcModule.CheckPermissions(npcId, m_host.OwnerID))
                    return new LSL_Rotation(Quaternion.Identity.X, Quaternion.Identity.Y, Quaternion.Identity.Z, Quaternion.Identity.W);

                ScenePresence sp = World.GetScenePresence(npcId);

                if (sp != null)
                {
                    Quaternion rot = sp.Rotation;
                    return new LSL_Rotation(rot.X, rot.Y, rot.Z, rot.W);
                }
            }

            return new LSL_Rotation(Quaternion.Identity.X, Quaternion.Identity.Y, Quaternion.Identity.Z, Quaternion.Identity.W);
        }

        public void osNpcSetRot(LSL_Key npc, LSL_Rotation rotation)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcSetRot");
            m_host.AddScriptLPS(1);

            INPCModule npcModule = World.RequestModuleInterface<INPCModule>();
            if (npcModule != null)
            {
                UUID npcId;
                if (!UUID.TryParse(npc.m_string, out npcId))
                    return;

                if (!npcModule.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                ScenePresence sp = World.GetScenePresence(npcId);

                if (sp != null)
                    sp.Rotation = rotation;
            }
        }

        public void osNpcStopMoveToTarget(LSL_Key npc)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcStopMoveToTarget");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId = new UUID(npc.m_string);

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.StopMoveToTarget(npcId, World);
            }
        }

        public void osNpcSay(LSL_Key npc, string message)
        {
            osNpcSay(npc, 0, message);
        }

        public void osNpcSay(LSL_Key npc, int channel, string message)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcSay");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId = new UUID(npc.m_string);

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.Say(npcId, World, message, channel);
            }
        }

        public void osNpcShout(LSL_Key npc, int channel, string message)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcShout");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId = new UUID(npc.m_string);

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.Shout(npcId, World, message, channel);
            }
        }

        public void osNpcSit(LSL_Key npc, LSL_Key target, int options)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcSit");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId = new UUID(npc.m_string);

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.Sit(npcId, new UUID(target.m_string), World);
            }
        }

        public void osNpcStand(LSL_Key npc)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcStand");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId = new UUID(npc.m_string);

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.Stand(npcId, World);
            }
        }

        public void osNpcRemove(LSL_Key npc)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcRemove");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId = new UUID(npc.m_string);

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.DeleteNPC(npcId, World);
            }
        }

        public void osNpcPlayAnimation(LSL_Key npc, string animation)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcPlayAnimation");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcID = new UUID(npc.m_string);

                if (module.CheckPermissions(npcID, m_host.OwnerID))
                    AvatarPlayAnimation(npcID.ToString(), animation);
            }
        }

        public void osNpcStopAnimation(LSL_Key npc, string animation)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcStopAnimation");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcID = new UUID(npc.m_string);

                if (module.CheckPermissions(npcID, m_host.OwnerID))
                    AvatarStopAnimation(npcID.ToString(), animation);
            }
        }

        public void osNpcWhisper(LSL_Key npc, int channel, string message)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcWhisper");
            m_host.AddScriptLPS(1);

            INPCModule module = World.RequestModuleInterface<INPCModule>();
            if (module != null)
            {
                UUID npcId = new UUID(npc.m_string);

                if (!module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                module.Whisper(npcId, World, message, channel);
            }
        }

        public void osNpcTouch(LSL_Key npcLSL_Key, LSL_Key object_key, LSL_Integer link_num)
        {
            CheckThreatLevel(ThreatLevel.High, "osNpcTouch");
            m_host.AddScriptLPS(1);
            
            INPCModule module = World.RequestModuleInterface<INPCModule>();
            int linkNum = link_num.value;
            if (module != null || (linkNum < 0 && linkNum != ScriptBaseClass.LINK_THIS))
            {
                UUID npcId;
                if (!UUID.TryParse(npcLSL_Key, out npcId) || !module.CheckPermissions(npcId, m_host.OwnerID))
                    return;

                SceneObjectPart part = null;
                UUID objectId;
                if (UUID.TryParse(LSL_String.ToString(object_key), out objectId))
                    part = World.GetSceneObjectPart(objectId);

                if (part == null)
                    return;

                if (linkNum != ScriptBaseClass.LINK_THIS)
                {
                    if (linkNum == 0 || linkNum == ScriptBaseClass.LINK_ROOT)
                    { // 0 and 1 are treated as root, find the root if the current part isnt it
                        if (!part.IsRoot)
                            part = part.ParentGroup.RootPart;
                    }
                    else
                    { // Find the prim with the given link number if not found then fail silently
                        part = part.ParentGroup.GetLinkNumPart(linkNum);
                        if (part == null)
                            return;
                    }
                }

                module.Touch(npcId, part.UUID);
            }
        }

        /// <summary>
        /// Save the current appearance of the script owner permanently to the named notecard.
        /// </summary>
        /// <param name="notecard">The name of the notecard to which to save the appearance.</param>
        /// <returns>The asset ID of the notecard saved.</returns>
        public LSL_Key osOwnerSaveAppearance(string notecard)
        {
            CheckThreatLevel(ThreatLevel.High, "osOwnerSaveAppearance");
            m_host.AddScriptLPS(1);

            return SaveAppearanceToNotecard(m_host.OwnerID, notecard);
        }

        public LSL_Key osAgentSaveAppearance(LSL_Key avatarId, string notecard)
        {
            CheckThreatLevel(ThreatLevel.VeryHigh, "osAgentSaveAppearance");
            m_host.AddScriptLPS(1);

            return SaveAppearanceToNotecard(avatarId, notecard);
        }

        protected LSL_Key SaveAppearanceToNotecard(ScenePresence sp, string notecard)
        {
            IAvatarFactoryModule appearanceModule = World.RequestModuleInterface<IAvatarFactoryModule>();

            if (appearanceModule != null)
            {
                appearanceModule.SaveBakedTextures(sp.UUID);
                OSDMap appearancePacked = sp.Appearance.Pack();

                TaskInventoryItem item
                    = SaveNotecard(notecard, "Avatar Appearance", Util.GetFormattedXml(appearancePacked as OSD), true);

                return new LSL_Key(item.AssetID.ToString());
            }
            else
            {
                return new LSL_Key(UUID.Zero.ToString());
            }
        }

        protected LSL_Key SaveAppearanceToNotecard(UUID avatarId, string notecard)
        {
            ScenePresence sp = World.GetScenePresence(avatarId);

            if (sp == null || sp.IsChildAgent)
                return new LSL_Key(UUID.Zero.ToString());

            return SaveAppearanceToNotecard(sp, notecard);
        }

        protected LSL_Key SaveAppearanceToNotecard(LSL_Key rawAvatarId, string notecard)
        {
            UUID avatarId;
            if (!UUID.TryParse(rawAvatarId, out avatarId))
                return new LSL_Key(UUID.Zero.ToString());

            return SaveAppearanceToNotecard(avatarId, notecard);
        }
        
        /// <summary>
        /// Get current region's map texture UUID
        /// </summary>
        /// <returns></returns>
        public LSL_Key osGetMapTexture()
        {
            CheckThreatLevel(ThreatLevel.None, "osGetMapTexture");
            m_host.AddScriptLPS(1);

            return m_ScriptEngine.World.RegionInfo.RegionSettings.TerrainImageID.ToString();
        }

        /// <summary>
        /// Get a region's map texture UUID by region UUID or name.
        /// </summary>
        /// <param name="regionName"></param>
        /// <returns></returns>
        public LSL_Key osGetRegionMapTexture(string regionName)
        {
            CheckThreatLevel(ThreatLevel.High, "osGetRegionMapTexture");
            m_host.AddScriptLPS(1);

            Scene scene = m_ScriptEngine.World;
            UUID key = UUID.Zero;
            GridRegion region;

            //If string is a key, use it. Otherwise, try to locate region by name.
            if (UUID.TryParse(regionName, out key))
                region = scene.GridService.GetRegionByUUID(UUID.Zero, key);
            else
                region = scene.GridService.GetRegionByName(UUID.Zero, regionName);

            // If region was found, return the regions map texture key.
            if (region != null)
                key = region.TerrainImage;

            ScriptSleep(1000);

            return key.ToString();
        }
        
       /// <summary>
        /// Return information regarding various simulator statistics (sim fps, physics fps, time
        /// dilation, total number of prims, total number of active scripts, script lps, various
        /// timing data, packets in/out, etc. Basically much the information that's shown in the
        /// client's Statistics Bar (Ctrl-Shift-1)
        /// </summary>
        /// <returns>List of floats</returns>
        public LSL_List osGetRegionStats()
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetRegionStats");
            m_host.AddScriptLPS(1);
            LSL_List ret = new LSL_List();
            float[] stats = World.StatsReporter.LastReportedSimStats;
            
            for (int i = 0; i < 21; i++)
            {
                ret.Add(new LSL_Float(stats[i]));
            }
            return ret;
        }

        public int osGetSimulatorMemory()
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetSimulatorMemory");
            m_host.AddScriptLPS(1);
            long pws = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;

            if (pws > Int32.MaxValue)
                return Int32.MaxValue;
            if (pws < 0)
                return 0;

            return (int)pws;
        }
        
        public void osSetSpeed(string UUID, LSL_Float SpeedModifier)
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osSetSpeed");
            m_host.AddScriptLPS(1);
            ScenePresence avatar = World.GetScenePresence(new UUID(UUID));

            if (avatar != null)
                avatar.SpeedModifier = (float)SpeedModifier;
        }
        
        public void osKickAvatar(string FirstName, string SurName, string alert)
        {
            CheckThreatLevel(ThreatLevel.Severe, "osKickAvatar");
            m_host.AddScriptLPS(1);

            World.ForEachRootScenePresence(delegate(ScenePresence sp)
            {
                if (sp.Firstname == FirstName && sp.Lastname == SurName)
                {
                    // kick client...
                    if (alert != null)
                        sp.ControllingClient.Kick(alert);

                    // ...and close on our side
                    sp.Scene.IncomingCloseAgent(sp.UUID, false);
                }
            });
        }

        public LSL_Float osGetHealth(string avatar)
        {
            CheckThreatLevel(ThreatLevel.None, "osGetHealth");
            m_host.AddScriptLPS(1);

            LSL_Float health = new LSL_Float(-1);
            ScenePresence presence = World.GetScenePresence(new UUID(avatar));
            if (presence != null) health = presence.Health;
            return health;
        }
        
        public void osCauseDamage(string avatar, double damage)
        {
            CheckThreatLevel(ThreatLevel.High, "osCauseDamage");
            m_host.AddScriptLPS(1);

            UUID avatarId = new UUID(avatar);
            Vector3 pos = m_host.GetWorldPosition();

            ScenePresence presence = World.GetScenePresence(avatarId); 
            if (presence != null)
            {
                LandData land = World.GetLandData(pos);
                if ((land.Flags & (uint)ParcelFlags.AllowDamage) == (uint)ParcelFlags.AllowDamage)
                {
                    float health = presence.Health;
                    health -= (float)damage;
                    presence.setHealthWithUpdate(health);
                    if (health <= 0)
                    {
                        float healthliveagain = 100;
                        presence.ControllingClient.SendAgentAlertMessage("You died!", true);
                        presence.setHealthWithUpdate(healthliveagain);
                        presence.Scene.TeleportClientHome(presence.UUID, presence.ControllingClient);
                    }
                }
            }
        }
        
        public void osCauseHealing(string avatar, double healing)
        {
            CheckThreatLevel(ThreatLevel.High, "osCauseHealing");
            m_host.AddScriptLPS(1);

            UUID avatarId = new UUID(avatar);
            ScenePresence presence = World.GetScenePresence(avatarId);
            Vector3 pos = m_host.GetWorldPosition();
            bool result = World.ScriptDanger(m_host.LocalId, new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z));
            if (result)
            {
                if (presence != null)
                {
                    float health = presence.Health;
                    health += (float)healing;
                    if (health >= 100)
                    {
                        health = 100;
                    }
                    presence.setHealthWithUpdate(health);
                }
            }
        }

        public LSL_List osGetPrimitiveParams(LSL_Key prim, LSL_List rules)
        {
            CheckThreatLevel(ThreatLevel.High, "osGetPrimitiveParams");
            m_host.AddScriptLPS(1);
            InitLSL();
            
            return m_LSL_Api.GetPrimitiveParamsEx(prim, rules);
        }

        public void osSetPrimitiveParams(LSL_Key prim, LSL_List rules)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetPrimitiveParams");
            m_host.AddScriptLPS(1);
            InitLSL();
            
            m_LSL_Api.SetPrimitiveParamsEx(prim, rules, "osSetPrimitiveParams");
        }
        
        /// <summary>
        /// Set parameters for light projection in host prim 
        /// </summary>
        public void osSetProjectionParams(bool projection, LSL_Key texture, double fov, double focus, double amb)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetProjectionParams");

            osSetProjectionParams(UUID.Zero.ToString(), projection, texture, fov, focus, amb);
        }

        /// <summary>
        /// Set parameters for light projection with uuid of target prim
        /// </summary>
        public void osSetProjectionParams(LSL_Key prim, bool projection, LSL_Key texture, double fov, double focus, double amb)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetProjectionParams");
            m_host.AddScriptLPS(1);

            SceneObjectPart obj = null;
            if (prim == UUID.Zero.ToString())
            {
                obj = m_host;
            }
            else
            {
                obj = World.GetSceneObjectPart(new UUID(prim));
                if (obj == null)
                    return;
            }

            obj.Shape.ProjectionEntry = projection;
            obj.Shape.ProjectionTextureUUID = new UUID(texture);
            obj.Shape.ProjectionFOV = (float)fov;
            obj.Shape.ProjectionFocus = (float)focus;
            obj.Shape.ProjectionAmbiance = (float)amb;

            obj.ParentGroup.HasGroupChanged = true;
            obj.ScheduleFullUpdate();
        }

        /// <summary>
        /// Like osGetAgents but returns enough info for a radar
        /// </summary>
        /// <returns>Strided list of the UUID, position and name of each avatar in the region</returns>
        public LSL_List osGetAvatarList()
        {
            CheckThreatLevel(ThreatLevel.None, "osGetAvatarList");
            m_host.AddScriptLPS(1);

            LSL_List result = new LSL_List();
            World.ForEachRootScenePresence(delegate (ScenePresence avatar)
            {
                if (avatar != null && avatar.UUID != m_host.OwnerID)
                {
                    result.Add(new LSL_String(avatar.UUID.ToString()));
                    OpenMetaverse.Vector3 ap = avatar.AbsolutePosition;
                    result.Add(new LSL_Vector(ap.X, ap.Y, ap.Z));
                    result.Add(new LSL_String(avatar.Name));
                }
            });

            return result;
        }

        /// <summary>
        /// Convert a unix time to a llGetTimestamp() like string
        /// </summary>
        /// <param name="unixTime"></param>
        /// <returns></returns>
        public LSL_String osUnixTimeToTimestamp(long time)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osUnixTimeToTimestamp");
            m_host.AddScriptLPS(1);

            long baseTicks = 621355968000000000;
            long tickResolution = 10000000;
            long epochTicks = (time * tickResolution) + baseTicks;
            DateTime date = new DateTime(epochTicks);

            return date.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
        }

        /// <summary>
        /// Get the description from an inventory item
        /// </summary>
        /// <param name="inventoryName"></param>
        /// <returns>Item description</returns>  
        public LSL_String osGetInventoryDesc(string item)
        {
            m_host.AddScriptLPS(1);

            lock (m_host.TaskInventory)
            {
                foreach (KeyValuePair<UUID, TaskInventoryItem> inv in m_host.TaskInventory)
                {
                    if (inv.Value.Name == item)
                    {
                        return inv.Value.Description.ToString();
                    }
                }
            }

            return String.Empty;
        }

        /// <summary>
        /// Invite user to the group this object is set to
        /// </summary>
        /// <param name="agentId"></param>
        /// <returns></returns>
        public LSL_Integer osInviteToGroup(LSL_Key agentId)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osInviteToGroup");
            m_host.AddScriptLPS(1);

            UUID agent = new UUID(agentId);

            // groups module is required
            IGroupsModule groupsModule = m_ScriptEngine.World.RequestModuleInterface<IGroupsModule>();
            if (groupsModule == null) return ScriptBaseClass.FALSE;

            // object has to be set to a group, but not group owned
            if (m_host.GroupID == UUID.Zero || m_host.GroupID == m_host.OwnerID) return ScriptBaseClass.FALSE;

            // object owner has to be in that group and required permissions
            GroupMembershipData member = groupsModule.GetMembershipData(m_host.GroupID, m_host.OwnerID);
            if (member == null || (member.GroupPowers & (ulong)GroupPowers.Invite) == 0) return ScriptBaseClass.FALSE;

            // check if agent is in that group already
            //member = groupsModule.GetMembershipData(agent, m_host.GroupID, agent);
            //if (member != null) return ScriptBaseClass.FALSE;

            // invited agent has to be present in this scene
            if (World.GetScenePresence(agent) == null) return ScriptBaseClass.FALSE;

            groupsModule.InviteGroup(null, m_host.OwnerID, m_host.GroupID, agent, UUID.Zero);

            return ScriptBaseClass.TRUE;
        }

        /// <summary>
        /// Eject user from the group this object is set to
        /// </summary>
        /// <param name="agentId"></param>
        /// <returns></returns>
        public LSL_Integer osEjectFromGroup(LSL_Key agentId)
        {
            CheckThreatLevel(ThreatLevel.VeryLow, "osEjectFromGroup");
            m_host.AddScriptLPS(1);

            UUID agent = new UUID(agentId);

            // groups module is required
            IGroupsModule groupsModule = m_ScriptEngine.World.RequestModuleInterface<IGroupsModule>();
            if (groupsModule == null) return ScriptBaseClass.FALSE;

            // object has to be set to a group, but not group owned
            if (m_host.GroupID == UUID.Zero || m_host.GroupID == m_host.OwnerID) return ScriptBaseClass.FALSE;

            // object owner has to be in that group and required permissions
            GroupMembershipData member = groupsModule.GetMembershipData(m_host.GroupID, m_host.OwnerID);
            if (member == null || (member.GroupPowers & (ulong)GroupPowers.Eject) == 0) return ScriptBaseClass.FALSE;

            // agent has to be in that group
            //member = groupsModule.GetMembershipData(agent, m_host.GroupID, agent);
            //if (member == null) return ScriptBaseClass.FALSE;

            // ejectee can be offline

            groupsModule.EjectGroupMember(null, m_host.OwnerID, m_host.GroupID, agent);

            return ScriptBaseClass.TRUE;
        }

        /// <summary>
        /// Sets terrain estate texture
        /// </summary>
        /// <param name="level"></param>
        /// <param name="texture"></param>
        /// <returns></returns>
        public void osSetTerrainTexture(int level, LSL_Key texture)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetTerrainTexture");

            m_host.AddScriptLPS(1);
            //Check to make sure that the script's owner is the estate manager/master
            //World.Permissions.GenericEstatePermission(
            if (World.Permissions.IsGod(m_host.OwnerID))
            {
                if (level < 0 || level > 3)
                    return;

                UUID textureID = new UUID();
                if (!UUID.TryParse(texture, out textureID))
                    return;

                // estate module is required
                IEstateModule estate = World.RequestModuleInterface<IEstateModule>();
                if (estate != null)
                    estate.setEstateTerrainBaseTexture(level, textureID);
            }
        }

        /// <summary>
        /// Sets terrain heights of estate
        /// </summary>
        /// <param name="corner"></param>
        /// <param name="low"></param>
        /// <param name="high"></param>
        /// <returns></returns>
        public void osSetTerrainTextureHeight(int corner, double low, double high)
        {
            CheckThreatLevel(ThreatLevel.High, "osSetTerrainTextureHeight");

            m_host.AddScriptLPS(1);
            //Check to make sure that the script's owner is the estate manager/master
            //World.Permissions.GenericEstatePermission(
            if (World.Permissions.IsGod(m_host.OwnerID))
            {
                if (corner < 0 || corner > 3)
                    return;

                // estate module is required
                IEstateModule estate = World.RequestModuleInterface<IEstateModule>();
                if (estate != null)
                    estate.setEstateTerrainTextureHeights(corner, (float)low, (float)high);
            }
        }

        #region Attachment commands

        public void osForceAttachToAvatar(int attachmentPoint)
        {
            CheckThreatLevel(ThreatLevel.High, "osForceAttachToAvatar");

            m_host.AddScriptLPS(1);

            InitLSL();
            ((LSL_Api)m_LSL_Api).AttachToAvatar(attachmentPoint);
        }

        public void osForceAttachToAvatarFromInventory(string itemName, int attachmentPoint)
        {
            CheckThreatLevel(ThreatLevel.High, "osForceAttachToAvatarFromInventory");

            m_host.AddScriptLPS(1);

            ForceAttachToAvatarFromInventory(m_host.OwnerID, itemName, attachmentPoint);
        }

        public void osForceAttachToOtherAvatarFromInventory(string rawAvatarId, string itemName, int attachmentPoint)
        {
            CheckThreatLevel(ThreatLevel.Severe, "osForceAttachToOtherAvatarFromInventory");

            m_host.AddScriptLPS(1);

            UUID avatarId;

            if (!UUID.TryParse(rawAvatarId, out avatarId))
                return;

            ForceAttachToAvatarFromInventory(avatarId, itemName, attachmentPoint);
        }

        public void ForceAttachToAvatarFromInventory(UUID avatarId, string itemName, int attachmentPoint)
        {
            IAttachmentsModule attachmentsModule = m_ScriptEngine.World.AttachmentsModule;

            if (attachmentsModule == null)
                return;

            InitLSL();

            TaskInventoryItem item = m_host.Inventory.GetInventoryItem(itemName);

            if (item == null)
            {
                ((LSL_Api)m_LSL_Api).llSay(0, string.Format("Could not find object '{0}'", itemName));
                throw new Exception(String.Format("The inventory item '{0}' could not be found", itemName));
            }

            if (item.InvType != (int)InventoryType.Object)
            {
                // FIXME: Temporary null check for regression tests since they dont' have the infrastructure to set
                // up the api reference.  
                if (m_LSL_Api != null)
                    ((LSL_Api)m_LSL_Api).llSay(0, string.Format("Unable to attach, item '{0}' is not an object.", itemName));

                throw new Exception(String.Format("The inventory item '{0}' is not an object", itemName));
            }

            ScenePresence sp = World.GetScenePresence(avatarId);

            if (sp == null)
                return;

            InventoryItemBase newItem = World.MoveTaskInventoryItem(sp.UUID, UUID.Zero, m_host, item.ItemID);

            if (newItem == null)
            {
                m_log.ErrorFormat(
                    "[OSSL API]: Could not create user inventory item {0} for {1}, attach point {2} in {3}",
                    itemName, m_host.Name, attachmentPoint, World.Name);

                return;
            }

            attachmentsModule.RezSingleAttachmentFromInventory(sp, newItem.ID, (uint)attachmentPoint);
        }

        public void osForceDetachFromAvatar()
        {
            CheckThreatLevel(ThreatLevel.High, "osForceDetachFromAvatar");

            m_host.AddScriptLPS(1);

            InitLSL();
            ((LSL_Api)m_LSL_Api).DetachFromAvatar();
        }

        public LSL_List osGetNumberOfAttachments(LSL_Key avatar, LSL_List attachmentPoints)
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osGetNumberOfAttachments");

            m_host.AddScriptLPS(1);

            UUID targetUUID;
            ScenePresence target;
            LSL_List resp = new LSL_List();

            if (attachmentPoints.Length >= 1 && UUID.TryParse(avatar.ToString(), out targetUUID) && World.TryGetScenePresence(targetUUID, out target))
            {
                foreach (object point in attachmentPoints.Data)
                {
                    LSL_Integer ipoint = new LSL_Integer(
                        (point is LSL_Integer || point is int || point is uint) ?
                            (int)point :
                            0
                    );
                    resp.Add(ipoint);
                    if (ipoint == 0)
                    {
                        // indicates zero attachments
                        resp.Add(new LSL_Integer(0));
                    }
                    else
                    {
                        // gets the number of attachments on the attachment point
                        resp.Add(new LSL_Integer(target.GetAttachments((uint)ipoint).Count));
                    }
                }
            }

            return resp;
        }

        public void osMessageAttachments(LSL_Key avatar, string message, LSL_List attachmentPoints, int options)
        {
            CheckThreatLevel(ThreatLevel.Moderate, "osMessageAttachments");
            m_host.AddScriptLPS(1);

            UUID targetUUID;
            ScenePresence target;

            if (attachmentPoints.Length >= 1 && UUID.TryParse(avatar.ToString(), out targetUUID) && World.TryGetScenePresence(targetUUID, out target))
            {
                List<int> aps = new List<int>();
                foreach (object point in attachmentPoints.Data)
                {
                    int ipoint;
                    if (int.TryParse(point.ToString(), out ipoint))
                    {
                        aps.Add(ipoint);
                    }
                }

                List<SceneObjectGroup> attachments = new List<SceneObjectGroup>();

                bool msgAll = aps.Contains(ScriptBaseClass.OS_ATTACH_MSG_ALL);
                bool invertPoints = (options & ScriptBaseClass.OS_ATTACH_MSG_INVERT_POINTS) != 0;

                if (msgAll && invertPoints)
                {
                    return;
                }
                else if (msgAll || invertPoints)
                {
                    attachments = target.GetAttachments();
                }
                else
                {
                    foreach (int point in aps)
                    {
                        if (point > 0)
                        {
                            attachments.AddRange(target.GetAttachments((uint)point));
                        }
                    }
                }

                // if we have no attachments at this point, exit now
                if (attachments.Count == 0)
                {
                    return;
                }

                List<SceneObjectGroup> ignoreThese = new List<SceneObjectGroup>();

                if (invertPoints)
                {
                    foreach (SceneObjectGroup attachment in attachments)
                    {
                        if (aps.Contains((int)attachment.AttachmentPoint))
                        {
                            ignoreThese.Add(attachment);
                        }
                    }
                }

                foreach (SceneObjectGroup attachment in ignoreThese)
                {
                    attachments.Remove(attachment);
                }
                ignoreThese.Clear();

                // if inverting removed all attachments to check, exit now
                if (attachments.Count < 1)
                {
                    return;
                }

                if ((options & ScriptBaseClass.OS_ATTACH_MSG_OBJECT_CREATOR) != 0)
                {
                    foreach (SceneObjectGroup attachment in attachments)
                    {
                        if (attachment.RootPart.CreatorID != m_host.CreatorID)
                        {
                            ignoreThese.Add(attachment);
                        }
                    }

                    foreach (SceneObjectGroup attachment in ignoreThese)
                    {
                        attachments.Remove(attachment);
                    }
                    ignoreThese.Clear();

                    // if filtering by same object creator removed all
                    //  attachments to check, exit now
                    if (attachments.Count == 0)
                    {
                        return;
                    }
                }

                if ((options & ScriptBaseClass.OS_ATTACH_MSG_SCRIPT_CREATOR) != 0)
                {
                    foreach (SceneObjectGroup attachment in attachments)
                    {
                        if (attachment.RootPart.CreatorID != m_item.CreatorID)
                        {
                            ignoreThese.Add(attachment);
                        }
                    }

                    foreach (SceneObjectGroup attachment in ignoreThese)
                    {
                        attachments.Remove(attachment);
                    }
                    ignoreThese.Clear();

                    // if filtering by object creator must match originating
                    //  script creator removed all attachments to check,
                    //  exit now
                    if (attachments.Count == 0)
                    {
                        return;
                    }
                }

                foreach (SceneObjectGroup attachment in attachments)
                {
                    MessageObject(attachment.RootPart.UUID, message);
                }
            }
        }

        #endregion

        /// <summary>
        /// Checks if thing is a UUID.
        /// </summary>
        /// <param name="thing"></param>
        /// <returns>1 if thing is a valid UUID, 0 otherwise</returns>
        public LSL_Integer osIsUUID(string thing)
        {
            CheckThreatLevel(ThreatLevel.None, "osIsUUID");
            m_host.AddScriptLPS(1);

            UUID test;
            return UUID.TryParse(thing, out test) ? 1 : 0;
        }

        /// <summary>
        /// Wraps to Math.Min()
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public LSL_Float osMin(double a, double b)
        {
            CheckThreatLevel(ThreatLevel.None, "osMin");
            m_host.AddScriptLPS(1);

            return Math.Min(a, b);
        }

        /// <summary>
        /// Wraps to Math.max()
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public LSL_Float osMax(double a, double b)
        {
            CheckThreatLevel(ThreatLevel.None, "osMax");
            m_host.AddScriptLPS(1);

            return Math.Max(a, b);
        }

        public LSL_Key osGetRezzingObject()
        {
            CheckThreatLevel(ThreatLevel.None, "osGetRezzingObject");
            m_host.AddScriptLPS(1);

            return new LSL_Key(m_host.ParentGroup.FromPartID.ToString());
        }
 
        /// <summary>
        /// Sets the response type for an HTTP request/response
        /// </summary>
        /// <returns></returns>
        public void osSetContentType(LSL_Key id, string type)
        {
            CheckThreatLevel(ThreatLevel.High,"osSetResponseType");
            if (m_UrlModule != null)
                m_UrlModule.HttpContentType(new UUID(id),type);
        }
        
   }
}