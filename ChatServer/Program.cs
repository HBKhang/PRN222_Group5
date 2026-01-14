using System.Net;
using System.Net.WebSockets;
using System.Text;

class ClientInfo
{
    public string Name { get; set; } = "";
    public WebSocket Socket { get; set; }
}

class Program
{
    static async Task Main()
    {
        HttpListener listener = new HttpListener();

        // Listen on all network interfaces
        listener.Prefixes.Add("http://+:5000/ws/");
        listener.Start();

        Console.WriteLine("WebSocket server started on ws://<ip>:5000/ws/");

        List<ClientInfo> clients = new();

        async Task Broadcast(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);

            foreach (var client in clients.ToList())
            {
                if (client.Socket.State == WebSocketState.Open)
                {
                    await client.Socket.SendAsync(
                        data,
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None
                    );
                }
            }
        }

        while (true)
        {
            HttpListenerContext context = await listener.GetContextAsync();

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            HttpListenerWebSocketContext wsContext =
                await context.AcceptWebSocketAsync(null);

            WebSocket socket = wsContext.WebSocket;

            ClientInfo client = new ClientInfo
            {
                Socket = socket
            };

            clients.Add(client);
            Console.WriteLine("Client connected");

            _ = Task.Run(async () =>
            {
                byte[] buffer = new byte[1024];

                try
                {
                    while (socket.State == WebSocketState.Open)
                    {
                        var result = await socket.ReceiveAsync(
                            buffer,
                            CancellationToken.None
                        );

                        if (result.MessageType == WebSocketMessageType.Close)
                            break;

                        string message = Encoding.UTF8.GetString(
                            buffer,
                            0,
                            result.Count
                        );

                        // Handle join message
                        if (message.StartsWith("__JOIN__:"))
                        {
                            client.Name = message.Replace("__JOIN__:", "");
                            await Broadcast($"🔵 {client.Name} joined the chat");
                        }
                        else
                        {
                            await Broadcast(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Client error: {ex.Message}");
                }
                finally
                {
                    clients.Remove(client);

                    if (!string.IsNullOrEmpty(client.Name))
                    {
                        await Broadcast($"🔴 {client.Name} disconnected");
                    }

                    try
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None
                        );
                    }
                    catch { }

                    Console.WriteLine("Client disconnected");
                }
            });
        }
    }
}
