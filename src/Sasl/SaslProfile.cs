﻿//  ------------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation
//  All rights reserved. 
//  
//  Licensed under the Apache License, Version 2.0 (the ""License""); you may not use this 
//  file except in compliance with the License. You may obtain a copy of the License at 
//  http://www.apache.org/licenses/LICENSE-2.0  
//  
//  THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, 
//  EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR 
//  CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR 
//  NON-INFRINGEMENT. 
// 
//  See the Apache Version 2.0 License for specific language governing permissions and 
//  limitations under the License.
//  ------------------------------------------------------------------------------------

namespace Amqp.Sasl
{
    using System;
    using Amqp.Framing;
    using Amqp.Types;

    abstract class SaslProfile
    {
        public void Open(string hostname, ITransport transport)
        {
            ProtocolHeader myHeader = this.Start(hostname, transport);

            ProtocolHeader theirHeader = Reader.ReadHeader(transport);
            Trace.WriteLine(TraceLevel.Frame, "RECV AMQP {0}", theirHeader);
            this.OnHeader(myHeader, theirHeader);

            SaslCode code = SaslCode.SysTemp;
            while (true)
            {
                ByteBuffer buffer = Reader.ReadFrameBuffer(transport, new byte[4], uint.MaxValue);
                if (buffer == null)
                {
                    throw new ObjectDisposedException(transport.GetType().Name);
                }

                if (!this.OnFrame(transport, buffer, out code))
                {
                    break;
                }
            }

            if (code != SaslCode.Ok)
            {
                throw new AmqpException(ErrorCode.UnauthorizedAccess,
                    Fx.Format(SRAmqp.SaslNegoFailed, code));
            }
        }

        public ProtocolHeader Start(string hostname, ITransport transport)
        {
            ProtocolHeader myHeader = new ProtocolHeader() { Id = 3, Major = 1, Minor = 0, Revision = 0 };

            ByteBuffer headerBuffer = new ByteBuffer(
                new byte[] { (byte)'A', (byte)'M', (byte)'Q', (byte)'P', myHeader.Id, myHeader.Major, myHeader.Minor, myHeader.Revision },
                0,
                8,
                8);
            transport.Send(headerBuffer);
            Trace.WriteLine(TraceLevel.Frame, "SEND AMQP {0}", myHeader);

            DescribedList command = this.GetStartCommand(hostname);
            if (command != null)
            {
                this.SendCommand(transport, command);
            }

            return myHeader;
        }

        public void OnHeader(ProtocolHeader myHeader, ProtocolHeader theirHeader)
        {
            if (theirHeader.Id != myHeader.Id || theirHeader.Major != myHeader.Major ||
                theirHeader.Minor != myHeader.Minor || theirHeader.Revision != myHeader.Revision)
            {
                throw new AmqpException(ErrorCode.NotImplemented, theirHeader.ToString());
            }
        }

        public bool OnFrame(ITransport transport, ByteBuffer buffer, out SaslCode code)
        {
            ushort channel;
            DescribedList command;
            Frame.GetFrame(buffer, out channel, out command);
            Trace.WriteLine(TraceLevel.Frame, "RECV {0}", command);

            bool shouldContinue = true;
            if (command.Descriptor.Code == Codec.SaslOutcome.Code)
            {
                code = ((SaslOutcome)command).Code;
                shouldContinue = false;
            }
            else
            {
                code = SaslCode.Ok;
                DescribedList response = this.OnCommand(command);
                if (response != null)
                {
                    this.SendCommand(transport, response);
                    shouldContinue = response.Descriptor.Code != Codec.SaslOutcome.Code;
                }
            }

            return shouldContinue;
        }

        protected abstract DescribedList GetStartCommand(string hostname);

        internal abstract DescribedList OnCommand(DescribedList command);

        void SendCommand(ITransport transport, DescribedList command)
        {
            ByteBuffer buffer = Frame.Encode(FrameType.Sasl, 0, command);
            transport.Send(buffer);
            Trace.WriteLine(TraceLevel.Frame, "SEND {0}", command);
        }
    }
}