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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ChatClient{
    public partial class MainWindow : Window
    {
        private ClientWebSocket socket = new ClientWebSocket();
        private CancellationTokenSource cts = new CancellationTokenSource();

        private string serverIp = "localhost";

        private readonly DispatcherTimer typingTimer;
        private DateTime lastTypingSentUtc = DateTime.MinValue;
        private const int TypingSendThrottleMs = 700;
        private const int TypingIndicatorHideMs = 1400;
        public MainWindow()
        {
            InitializeComponent();

            typingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TypingIndicatorHideMs)
            };
            typingTimer.Tick += (_, __) =>
            {
                TypingIndicator.Visibility = Visibility.Collapsed;
                TypingText.Text = string.Empty;
                typingTimer.Stop();
            };
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
            await SendTyping(false);
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
                        if (TryHandleTyping(msg))
                        {
                            return;
                        }

                        if (msg.StartsWith("__IMAGE__:"))
                        {
                            string fileName = msg.Replace("__IMAGE__:", "");

                            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri($"http://{serverIp}:5000/images/{fileName}");
                            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                            bitmap.EndInit();

                            ChatList.Items.Add(BuildImageBubble(bitmap, ParseSenderName(msg) ?? "", isMine: false));
                        }
                        else
                        {
                            var (senderName, body) = SplitSenderAndBody(msg);
                            bool isMine = IsMine(senderName);
                            ChatList.Items.Add(BuildTextBubble(senderName, body, isMine));
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

        private bool IsMine(string? senderName)
        {
            var me = NameBox.Text.Trim();
            if (string.IsNullOrEmpty(me) || string.IsNullOrEmpty(senderName))
                return false;

            return string.Equals(me, senderName, StringComparison.OrdinalIgnoreCase);
        }

        private (string sender, string body) SplitSenderAndBody(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
                return (string.Empty, string.Empty);

            int idx = msg.IndexOf(':');
            if (idx <= 0)
                return (string.Empty, msg);

            string sender = msg.Substring(0, idx).Trim();
            string body = idx + 1 < msg.Length ? msg[(idx + 1)..].TrimStart() : string.Empty;
            return (sender, body);
        }

        private string? ParseSenderName(string msg)
        {
            var (sender, _) = SplitSenderAndBody(msg);
            return string.IsNullOrWhiteSpace(sender) ? null : sender;
        }

        private UIElement BuildTextBubble(string senderName, string text, bool isMine)
        {
            var outer = new Grid
            {
                Margin = new Thickness(0, 2, 0, 2)
            };

            var bubble = new Border
            {
                Background = (Brush)FindResource(isMine ? "BrushBubbleMe" : "BrushBubbleOther"),
                BorderBrush = (Brush)FindResource("BrushBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 7, 10, 7),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.08,
                    BlurRadius = 12,
                    ShadowDepth = 1
                },
                MaxWidth = 340,
                HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };

            var stack = new StackPanel();

            if (!string.IsNullOrWhiteSpace(senderName) && !isMine)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = senderName,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("BrushSubtle"),
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("BrushText"),
                FontSize = 13
            });

            bubble.Child = stack;
            outer.Children.Add(bubble);
            return outer;
        }

        private UIElement BuildImageBubble(BitmapImage bitmap, string senderName, bool isMine)
        {
            var outer = new Grid
            {
                Margin = new Thickness(0, 2, 0, 2)
            };

            var bubble = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = (Brush)FindResource("BrushBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8),
                MaxWidth = 340,
                HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.08,
                    BlurRadius = 12,
                    ShadowDepth = 1
                }
            };

            var stack = new StackPanel();

            if (!string.IsNullOrWhiteSpace(senderName) && !isMine)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = senderName,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("BrushSubtle"),
                    Margin = new Thickness(2, 0, 0, 6)
                });
            }

            stack.Children.Add(new Image
            {
                Width = 220,
                Stretch = Stretch.Uniform,
                Source = bitmap
            });

            bubble.Child = stack;
            outer.Children.Add(bubble);
            return outer;
        }

        private bool TryHandleTyping(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg) || !msg.StartsWith("__TYPING__:", StringComparison.Ordinal))
                return false;

            // Format: __TYPING__:Name:true|false
            var payload = msg.Substring("__TYPING__:".Length);
            var parts = payload.Split(':');
            if (parts.Length < 2)
                return true;

            var name = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(name) || IsMine(name))
                return true;

            bool isTyping = bool.TryParse(parts[1].Trim(), out var v) && v;

            if (!isTyping)
            {
                TypingIndicator.Visibility = Visibility.Collapsed;
                TypingText.Text = string.Empty;
                typingTimer.Stop();
                return true;
            }

            TypingText.Text = $"{name} is typing";
            TypingIndicator.Visibility = Visibility.Visible;
            typingTimer.Stop();
            typingTimer.Start();
            return true;
        }

        private async Task SendTyping(bool isTyping)
        {
            if (socket.State != WebSocketState.Open)
                return;

            var name = NameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (isTyping)
            {
                var now = DateTime.UtcNow;
                if ((now - lastTypingSentUtc).TotalMilliseconds < TypingSendThrottleMs)
                    return;
                lastTypingSentUtc = now;
            }

            await SendRaw($"__TYPING__:{name}:{isTyping.ToString().ToLowerInvariant()}");
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

        private async void MessageBoxInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            // best-effort typing indicator
            if (socket.State != WebSocketState.Open)
                return;

            string text = MessageBoxInput.Text;
            await SendTyping(!string.IsNullOrWhiteSpace(text));
        }
    }
}