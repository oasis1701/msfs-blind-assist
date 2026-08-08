// CRITICAL: Send() is called on vPilot's own event thread. The vPilot-to-TTS original
// called pipe.Connect(500) there, so with nothing listening every VATSIM event cost
// vPilot a 500 ms stall. Here the event thread only ever enqueues; one background
// thread owns the pipe and backs off when no one is home. This is what makes leaving
// the plugin installed with the feature switched off genuinely free.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using MSFSBlindAssist.VPilot;

namespace MSFSBlindAssist.VPilotPlugin
{
    internal sealed class PipeClient : IDisposable
    {
        private const string PipeName = "MSFSBlindAssist.vPilot";
        private const int ConnectTimeoutMs = 100;
        private const int MaxQueue = 200;
        private const int IdlePollMs = 200;
        private const int BackoffMs = 5000;
        private const int FailuresBeforeBackoff = 3;

        private readonly Queue<string> _queue = new Queue<string>();
        private readonly object _gate = new object();
        private readonly Thread _sender;
        private volatile bool _running = true;

        private NamedPipeClientStream _pipe;
        private StreamWriter _writer;
        private int _consecutiveFailures;

        public PipeClient()
        {
            _sender = new Thread(SendLoop);
            _sender.IsBackground = true;
            _sender.Name = "MSFSBA-vPilot-pipe";
            _sender.Start();
        }

        /// <summary>Called on vPilot's event thread. Enqueues and returns; never blocks.</summary>
        public void Send(string type, string from, string message)
        {
            string line = VPilotWireFormat.Encode(type, from, message);
            lock (_gate)
            {
                // Drop oldest. A backlog means nobody is listening, and the newest
                // transmission is the one worth hearing.
                while (_queue.Count >= MaxQueue)
                    _queue.Dequeue();
                _queue.Enqueue(line);
                Monitor.Pulse(_gate);
            }
        }

        private void SendLoop()
        {
            while (_running)
            {
                string line = null;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                        Monitor.Wait(_gate, IdlePollMs);
                    if (_queue.Count > 0)
                        line = _queue.Peek();
                }

                if (line == null)
                    continue;

                if (TryWrite(line))
                {
                    lock (_gate)
                    {
                        // Dequeue only the item we actually wrote. Send()'s drop-oldest eviction can
                        // shift a different, never-sent message to the head while the write is in
                        // flight outside the lock — an unconditional Dequeue would discard that one.
                        if (_queue.Count > 0 && ReferenceEquals(_queue.Peek(), line))
                            _queue.Dequeue();
                    }
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= FailuresBeforeBackoff)
                    {
                        // Nobody is listening — MSFS Blind Assist is closed or the
                        // feature is off. Stop hammering the pipe, and drop the backlog
                        // so a later connect doesn't replay stale chatter.
                        lock (_gate) { _queue.Clear(); }
                        Thread.Sleep(BackoffMs);
                    }
                }
            }

            Disconnect();
        }

        private bool TryWrite(string line)
        {
            try
            {
                if (_pipe == null || !_pipe.IsConnected)
                    Connect();

                _writer.WriteLine(line);
                _writer.Flush();
                return true;
            }
            catch (Exception ex)
            {
                if (_consecutiveFailures == 0)
                    PluginLog.Error("Pipe send failed: " + ex.Message);
                Disconnect();
                return false;
            }
        }

        private void Connect()
        {
            Disconnect();
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            _pipe.Connect(ConnectTimeoutMs);
            _writer = new StreamWriter(_pipe);
            PluginLog.Info("Connected to MSFS Blind Assist");
        }

        private void Disconnect()
        {
            if (_writer != null)
            {
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }
            if (_pipe != null)
            {
                try { _pipe.Dispose(); } catch { }
                _pipe = null;
            }
        }

        public void Dispose()
        {
            _running = false;
            lock (_gate) { Monitor.Pulse(_gate); }
        }
    }
}
