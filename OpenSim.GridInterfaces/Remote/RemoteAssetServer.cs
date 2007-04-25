using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;
using libsecondlife;
using OpenSim.Framework.Interfaces;
using OpenSim.Framework.Types;
using OpenSim.Framework.Utilities;

namespace OpenSim.GridInterfaces.Remote
{
    public class RemoteAssetServer : IAssetServer
    {
        private IAssetReceiver _receiver;
        private BlockingQueue<ARequest> _assetRequests;
        private Thread _remoteAssetServerThread;
        private string AssetServerUrl;
        private string AssetSendKey;

        public RemoteAssetServer()
        {
            this._assetRequests = new BlockingQueue<ARequest>();
            this._remoteAssetServerThread = new Thread(new ThreadStart(RunRequests));
            this._remoteAssetServerThread.IsBackground = true;
            this._remoteAssetServerThread.Start();
            OpenSim.Framework.Console.MainConsole.Instance.WriteLine("Remote Asset Server class created");
        }

        public void SetReceiver(IAssetReceiver receiver)
        {
            this._receiver = receiver;
        }

        public void RequestAsset(LLUUID assetID, bool isTexture)
        {
            ARequest req = new ARequest();
            req.AssetID = assetID;
            req.IsTexture = isTexture;
            this._assetRequests.Enqueue(req);
        }

        public void UpdateAsset(AssetBase asset)
        {

        }

        public void UploadNewAsset(AssetBase asset)
        {

        }

        public void SetServerInfo(string ServerUrl, string ServerKey)
        {
            this.AssetServerUrl = ServerUrl;
            this.AssetSendKey = ServerKey;
        }

        private void RunRequests()
        {
            while (true)
            {
                //we need to add support for the asset server not knowing about a requested asset
                ARequest req = this._assetRequests.Dequeue();
                LLUUID assetID = req.AssetID;
                OpenSim.Framework.Console.MainConsole.Instance.WriteLine(" RemoteAssetServer- Got a AssetServer request, processing it - " + this.AssetServerUrl + "getasset/" + AssetSendKey + "/" + assetID + "/data");
                WebRequest AssetLoad = WebRequest.Create(this.AssetServerUrl + "getasset/" + AssetSendKey + "/" + assetID + "/data");
                WebResponse AssetResponse = AssetLoad.GetResponse();
                byte[] idata = new byte[(int)AssetResponse.ContentLength];
                BinaryReader br = new BinaryReader(AssetResponse.GetResponseStream());
                idata = br.ReadBytes((int)AssetResponse.ContentLength);
                br.Close();

                AssetBase asset = new AssetBase();
                asset.FullID = assetID;
                asset.Data = idata;
                _receiver.AssetReceived(asset, req.IsTexture);
            }
        }

        public void Close()
        {

        }
    }

    public class RemoteAssetPlugin : IAssetPlugin
    {
        public RemoteAssetPlugin()
        {

        }

        public IAssetServer GetAssetServer()
        {
            return (new RemoteAssetServer());
        }
    }

}
