﻿using System.Threading.Tasks;
using Uragano.Abstractions;

namespace Uragano.Remoting
{
	public delegate Task ReceiveMessageHandler(IMessageSender sender, TransportMessage<ResultMessage> message);
	public interface IMessageListener
	{
		event ReceiveMessageHandler OnReceived;

		Task Received(IMessageSender sender, TransportMessage<ResultMessage> message);
	}
}
