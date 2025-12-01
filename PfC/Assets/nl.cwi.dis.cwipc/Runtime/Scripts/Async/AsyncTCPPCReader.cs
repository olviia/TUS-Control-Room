using UnityEngine;
using System;
using System.Text;
using System.Collections.Generic;
using Cwipc;

namespace Cwipc
{
    using IncomingTileDescription = Cwipc.StreamSupport.IncomingTileDescription;

    public class AsyncTCPPCReader : AsyncTCPReader
    {
        public AsyncTCPPCReader() : base()
        {
        }
        public AsyncTCPPCReader(string _url, string fourcc, IncomingTileDescription[] _tileDescriptors)
        : this()
        {
            Init(_url, fourcc, _tileDescriptors);
        }

        public AsyncTCPPCReader Init(string _url, string fourcc, IncomingTileDescription[] _tileDescriptors)
        {
            Init(_url);
            lock (this)
            {
                int nTiles = _tileDescriptors.Length;
                receivers = new ReceiverInfo[nTiles];
                for (int ti = 0; ti < nTiles; ti++)
                {
                    ReceiverInfo ri = new ReceiverInfo();
                    ri.tileNumber = ti;
                    ri.host = url.Host;
                    // ri.port = url.Port + _tileDescriptors[ti].tileNumber;
                    ri.port = url.Port + ti;
                    IncomingTileDescription td = _tileDescriptors[ti];
                    ri.tileDescriptor = td;
                    ri.outQueue = _tileDescriptors[ti].outQueue;
                    ri.fourcc = StreamSupport.VRT_4CC(fourcc[0], fourcc[1], fourcc[2], fourcc[3]);
                    receivers[ti] = ri;
                }
                Start();
                initialized = true;
            }
            return this;
        }

        public void setTileQualityIndex(int tileIndex, int qualityIndex)
        {
            Debug.Log($"{Name()}: setTileQualityIndex({tileIndex},{qualityIndex})");
            int portOffset = qualityIndex * receivers.Length;
            receivers[tileIndex].portOffset = portOffset;
        }
    }
}