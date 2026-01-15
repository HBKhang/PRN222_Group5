using Microsoft.Win32;
using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace ChatClient{
    public partial class MainWindow : Window
    {
        private ClientWebSocket socket = new ClientWebSocket();
        private CancellationTokenSource cts = new CancellationTokenSource();

        private string serverIp = "localhost";
        public MainWindow()
        {
            InitializeComponent();
        }
        //Connect
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    MessageBox.Show("Already connected");
                    return;
                }

                socket = new ClientWebSocket();

                await socket.ConnectAsync(
                    new Uri($"ws://{serverIp}:5000/ws/"),
                    CancellationToken.None
                );

                string name = NameBox.Text.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("Enter a name");
                    return;
                }

                await SendRaw($"__JOIN__:{name}");

                _ = ReceiveMessages();

                StatusText.Text = "Connected";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Connect failed: " + ex.Message);
            }
        }
        //Send text
        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            if (socket.State != WebSocketState.Open)
                return;

            string msg = MessageBoxInput.Text.Trim();
            if (string.IsNullOrEmpty(msg))
                return;

            await SendRaw($"{NameBox.Text}: {msg}");
            MessageBoxInput.Clear();
        }
        private async Task SendRaw(string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);

            await socket.SendAsync(
                data,
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }
        //Receive
        private async Task ReceiveMessages()
        {
            byte[] buffer = new byte[4096];

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

                    string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    Dispatcher.Invoke(() =>
                    {
                        if (msg.StartsWith("__IMAGE__:"))
                        {
                            string fileName = msg.Replace("__IMAGE__:", "");

                            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri($"http://{serverIp}:5000/images/{fileName}");
                            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                            bitmap.EndInit();

                            Image img = new Image
                            {
                                Width = 200,
                                Margin = new Thickness(5),
                                Source = bitmap
                            };

                            ChatList.Items.Add(img);
                        }
                        else
                        {
                            ChatList.Items.Add(new TextBlock
                            {
                                Text = msg,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(5)
                            });
                        }

                        ChatList.ScrollIntoView(ChatList.Items[^1]);
                    });
                }
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    ChatList.Items.Add("⚠ Connection closed");
                });
            }
        }
        //Files, images upload
        private async void Upload_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Multiselect = false;

            if (dialog.ShowDialog() != true)
                return;

            string filePath = dialog.FileName;
            string fileName = Path.GetFileName(filePath);

            try
            {
                using HttpClient client = new HttpClient();
                using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

                HttpRequestMessage request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"http://{serverIp}:5000/upload/"
                );

                request.Headers.Add("File-Name", fileName);
                request.Content = new StreamContent(fs);
                request.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(GetContentType(filePath));

                HttpResponseMessage response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    ChatList.Items.Add($"📤 You uploaded: {fileName}");
                }
                else
                {
                    MessageBox.Show("Upload failed");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Upload error: " + ex.Message);
            }
        }
        //Content type
        private string GetContentType(string path)
        {
            string ext = Path.GetExtension(path).ToLower();

            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };
        }
        //Close
        protected override async void OnClosed(EventArgs e)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client closing",
                        CancellationToken.None
                    );
                }
            }
            catch { }

            base.OnClosed(e);
        }
        private void MessageBoxInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Send_Click(sender, e);
            }
        }
    }
}