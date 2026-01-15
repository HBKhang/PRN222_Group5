using System.Net;
using System.Net.WebSockets;
using System.Text;

class ClientInfo
{
    public string Name { get; set; } = "";
    public WebSocket? Socket { get; set; }
}

class Program
{
    static async Task Main()
    {
        HttpListener listener = new HttpListener();

        listener.Prefixes.Add("http://+:5000/upload/");
        listener.Prefixes.Add("http://+:5000/ws/");
        listener.Prefixes.Add("http://+:5000/images/");

        listener.Start();

        Console.WriteLine("WebSocket server started on ws://<ip>:5000/ws/");

        List<ClientInfo> clients = new();

        async Task Broadcast(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);

            foreach (var client in clients.ToList())
            {
                if (client.Socket != null && client.Socket.State == WebSocketState.Open)
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

            //File upload
            if (context.Request.Url?.AbsolutePath == "/upload/")
            {
                string? fileName = context.Request.Headers["File-Name"];
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                string contentType = context.Request.Headers["Content-Type"] ?? "";
                bool isImage = contentType.StartsWith("image/");

                string folder = isImage ? "Uploads/Images" : "Uploads/Files";
                Directory.CreateDirectory(folder);

                string savePath = Path.Combine(folder, fileName);

                using FileStream fs = new FileStream(savePath, FileMode.Create);
                await context.Request.InputStream.CopyToAsync(fs);

                context.Response.StatusCode = 200;
                context.Response.Close();

                Console.WriteLine($"Uploaded: {fileName}");

                if (isImage)
                    await Broadcast($"__IMAGE__:{fileName}");
                else
                    await Broadcast($"📁 File uploaded: {fileName}");

                continue;
            }

            //Image upload
            if (context.Request.Url?.AbsolutePath.StartsWith("/images/") == true)
            {
                string fileName = Path.GetFileName(context.Request.Url.AbsolutePath);
                string filePath = Path.Combine("Uploads/Images", fileName);

                if (!File.Exists(filePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                context.Response.ContentType = "image/*";

                using FileStream fs = File.OpenRead(filePath);
                await fs.CopyToAsync(context.Response.OutputStream);

                context.Response.Close();
                continue;
            }

            //Websocket
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
