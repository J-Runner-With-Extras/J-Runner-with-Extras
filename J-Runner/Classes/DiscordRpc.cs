using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;

namespace JRunner.Classes
{
    internal class DiscordRpc
    {
        private static NamedPipeClientStream pipe;

        public static void Connect()
        {
            // TODO this needs to be filled in
            string clientId = "";

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut);
                    pipe.Connect(100);
                    break;
                }
                catch (Exception ex)
                {
                    if (variables.debugMode) Console.WriteLine("Discord RPC Fail: " + ex.Message);
                }
            }

            string handshake = "{\"v\":1,\"client_id\":\"" + clientId + "\"}";
            Send(0, handshake); // opcode 0 = handshake

            // Wait for READY after handshake so we know the pipe is usable
            try
            {
                DateTime waitUntil = DateTime.UtcNow.AddSeconds(2);
                pipe.ReadTimeout = 2000;

                while (DateTime.UtcNow < waitUntil)
                {
                    byte[] header = new byte[8];
                    int read = 0;
                    while (read < header.Length)
                    {
                        int r = pipe.Read(header, read, header.Length - read);
                        if (r == 0) throw new IOException("Discord RPC pipe closed during READY wait.");
                        read += r;
                    }

                    int opcode = BitConverter.ToInt32(header, 0);
                    int length = BitConverter.ToInt32(header, 4);
                    if (length < 0 || length > 1024 * 1024) throw new IOException("Discord RPC invalid payload length.");

                    byte[] payload = new byte[length];
                    read = 0;
                    while (read < length)
                    {
                        int r = pipe.Read(payload, read, length - read);
                        if (r == 0) throw new IOException("Discord RPC pipe closed during READY wait.");
                        read += r;
                    }

                    string json = Encoding.UTF8.GetString(payload);
                    if (opcode == 1 && json.Contains("\"evt\":\"READY\""))
                    {
                        break;
                    }

                    if (opcode == 1 && json.Contains("\"evt\":\"ERROR\""))
                    {
                        if (variables.debugMode) Console.WriteLine("Discord RPC handshake error: " + json);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (variables.debugMode) Console.WriteLine("Discord RPC READY wait failed: " + ex.Message);
            }
        }

        private static void Send(int opcode, string json)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);

            byte[] header = new byte[8];

            BitConverter.GetBytes(opcode).CopyTo(header, 0);
            BitConverter.GetBytes(data.Length).CopyTo(header, 4);

            try
            {
                pipe.Write(header, 0, 8);
                pipe.Write(data, 0, data.Length);
                pipe.Flush();
            }
            catch(Exception ex)
            {
                if (variables.debugMode) Console.WriteLine("Discord RPC Fail: " + ex.Message);
            }
        }

        public static void SetIdle()
        {
            SetPresence("Idle", "No Image Selected");
        }

        public static void SetPresence(string whatimdoing, string imagetype)
        {
            // If Discord rich presence support is *disabled*, this is a no-op
            if (!variables.DiscordRpc)
            {
                return;
            }

            string nonce = Guid.NewGuid().ToString();
            string json = "";

            if (pipe == null || !pipe.IsConnected)
            {
                Connect();
            }

            json =
                "{"
                + "\"cmd\":\"SET_ACTIVITY\","
                + "\"nonce\":\"" + nonce + "\","
                + "\"args\":{"
                + "\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + ","
                + "\"activity\":{"
                + "\"details\":\"" + whatimdoing + "\","
                + "\"state\":\"" + imagetype + "\","
                + "\"assets\":{"
                + "\"large_text\":\"J-Runner With Extras\","
                + "\"large_image\":\"app_icon\""
                + "}"
                + "}"
                + "}"
                + "}";

            Send(1, json);
        }

        public static void Close()
        {
            if (pipe != null)
                pipe.Dispose();
        }
    }
}
