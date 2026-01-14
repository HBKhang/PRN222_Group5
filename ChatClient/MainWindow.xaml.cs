using System.Net.WebSockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ChatClient
{
    public partial class MainWindow : Window
    {
        ClientWebSocket socket;

        public MainWindow()
        {
            InitializeComponent();
        }

        async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(UserBox.Text))
            {
                MessageBox.Show("Enter your name first");
                return;
            }

            socket = new ClientWebSocket();

            try
            {
                await socket.ConnectAsync(
                    new Uri("ws://26.227.154.122:5000/ws/"), // CHANGE IP
                    CancellationToken.None
                );

                ChatList.Items.Add("Connected to server");
                ReceiveMessages();
            }
            catch (WebSocketException)
            {
                // Normal disconnect — ignore
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }

        }

        async void ReceiveMessages()
        {
            byte[] buffer = new byte[1024];

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(
                    buffer,
                    CancellationToken.None
                );

                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);

                Dispatcher.Invoke(() =>
                {
                    bool shouldScroll = IsUserAtBottom();
                    ChatList.Items.Add(msg);

                    if (shouldScroll)
                        ChatList.ScrollIntoView(ChatList.Items[^1]);
                });

            }
        }
        bool IsUserAtBottom()
        {
            if (ChatList.Items.Count == 0)
                return true;

            ChatList.UpdateLayout();
            var border = VisualTreeHelper.GetChild(ChatList, 0) as Decorator;
            var scroll = border.Child as ScrollViewer;

            return scroll.VerticalOffset >= scroll.ScrollableHeight;
        }


        async void Send_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }
        private void MessageInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.IsKeyDown(Key.LeftShift))
            {
                e.Handled = true; // STOP newline
                SendMessage();
            }
        }
        async void SendMessage()
        {
            if (socket == null || socket.State != WebSocketState.Open)
                return;

            if (string.IsNullOrWhiteSpace(MessageInput.Text))
                return;

            string message = $"{UserBox.Text}: {MessageInput.Text}";
            byte[] data = Encoding.UTF8.GetBytes(message);

            await socket.SendAsync(
                data,
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            MessageInput.Clear();
        }
        private void EmojiBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmojiBox.SelectedItem is ComboBoxItem item)
            {
                string emoji = item.Content.ToString();

                int caret = MessageInput.CaretIndex;
                MessageInput.Text =
                    MessageInput.Text.Insert(caret, emoji);

                MessageInput.CaretIndex = caret + emoji.Length;
                MessageInput.Focus();

                EmojiBox.SelectedIndex = -1; // reset selection
            }
        }
        protected override async void OnClosed(EventArgs e)
        {
            if (socket != null && socket.State == WebSocketState.Open)
            {
                try
                {
                    if (socket.State == WebSocketState.Open ||
                        socket.State == WebSocketState.CloseReceived)
                    {
                        try
                        {
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Closing",
                                CancellationToken.None
                            );
                        }
                        catch { }
                    }

                }
                catch { }
            }

            base.OnClosed(e);
        }

    }
}