﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Globalization.DateTimeFormatting;
using Pusher.Events;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Pusher.Connections.WindowsStore
{
    public class WebSocketConnection : IConnection, IDisposable
    {
        private MessageWebSocket _socket;
        private DataWriter _messageWriter;
        private readonly Uri _endpoint;
        private ConnectionState _connectionState;

        public WebSocketConnection(Uri endpoint)
        {
            _endpoint = endpoint;
            SetupSocket();
        }

        private void SetupSocket()
        {
            _socket = new MessageWebSocket();
            _socket.Control.MessageType = SocketMessageType.Utf8;
            _socket.Closed += OnSocketClosed;
            _socket.MessageReceived += OnMessageReceived;
        }

        private async void OnMessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            if (OnData == null) return;

	        Exception exception = null;
            try
            {
                var reader = args.GetDataReader();
                var text = reader.ReadString(reader.UnconsumedBufferLength);
                OnData(sender, new DataReceivedEventArgs { TextData = text });
            }
            catch (Exception e)
            {
                exception = e;
            }
			// cannot await in catch
            if (exception != null)
            {
                _connectionState = ConnectionState.Failed;
                try
                {
                    await Reconnect();
                }
                catch (Exception e)
                {
                    Error(e);
                }
            }
        }

        private async Task Reconnect()
        {
            if (_connectionState == ConnectionState.Connecting || _connectionState == ConnectionState.Connected) return;

            SetupSocket();
            await Open();
        }
        
        private async void OnSocketClosed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            _messageWriter = null;
            if (_connectionState != ConnectionState.Disconnecting)
            {
                _connectionState = ConnectionState.Failed;
                try
                {
                    await Reconnect();
                }
                catch (Exception e)
                {
                    Error(e);
                    return;
                }
            }
			_connectionState = ConnectionState.Disconnected;
            if (OnClose != null) OnClose(sender, new EventArgs());
        }

        #region Implementation of IConnection

        public void Close()
        {
			_connectionState = ConnectionState.Disconnecting;
            _socket.Close(1000, "Close requested");
			_connectionState = ConnectionState.Disconnected;
        }

        public async Task Open()
        {
            if (_connectionState == ConnectionState.Connected)
            {
                Close();
                SetupSocket();
            }
			_connectionState = ConnectionState.Connecting;
            await _socket.ConnectAsync(_endpoint);
            _messageWriter = new DataWriter(_socket.OutputStream);
			_connectionState = ConnectionState.Connected;
			if (OnOpen != null)
            {
                OnOpen(this, new EventArgs());
            }
        }

        public async Task SendMessage(string data)
        {
            if (_messageWriter == null)
            {
                await Open();
            }
            Debug.Assert(_messageWriter != null);

            _messageWriter.WriteString(data);
            await _messageWriter.StoreAsync();
        }
        
        public event EventHandler<ExceptionEventArgs> OnError;

        /// <summary>
        /// Triggered whenever an error occurs whenever we cannot raise an exception (because it could not be caught).
        /// </summary>
        private void Error(Exception e)
        {
            if (OnError == null) return;
            OnError(this, new ExceptionEventArgs { Exception = e });
        }

        public event EventHandler<EventArgs> OnClose;
        public event EventHandler<EventArgs> OnOpen;
        public event EventHandler<DataReceivedEventArgs> OnData;

        #endregion

        #region Implementation of IDisposable

        public void Dispose()
        {
            _socket.Dispose();
        }

        #endregion
    }
}