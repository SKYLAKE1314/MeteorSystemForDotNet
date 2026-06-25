Imports System.Windows
Imports VAT.Common.VATJsonObject

Partial Public Class ProcessPage

    Private ReadOnly _ws As New WebSocketManager()
    Private ReadOnly _server As New WebSocketServer()

    Private ReadOnly _client As New WebSocketClient()
    Public Sub New()

        InitializeComponent()

        AddHandler Me.Loaded,
            AddressOf Page_Loaded

    End Sub

    Private Sub Page_Loaded(
    sender As Object,
    e As RoutedEventArgs)

        AddHandler _ws.MessageReceived,
        AddressOf OnMessageReceived

    End Sub

    Private _serverStarted As Boolean = False

    Private Sub StartServer_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            If _serverStarted Then
                AddLog("Server 已啟動")
                Return
            End If

            _serverStarted = True

            Dim port As Integer =
            Integer.Parse(PortBox.Text)

            AddLog($"Server Started : 0.0.0.0:{port}")

            Task.Run(
            Async Function()

                Await _ws.StartServer(port)

            End Function)

        Catch ex As Exception

            AddLog(ex.Message)

        End Try

    End Sub

    Private Sub StopServer_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            _ws.StopServer()

            _serverStarted = False

            AddLog("Server Stopped")

        Catch ex As Exception

            AddLog(ex.Message)

        End Try

    End Sub
    Private Async Sub Broadcast_Click(
    sender As Object,
    e As RoutedEventArgs)

        Try

            Await _ws.Broadcast(
            SendBox.Text)

            AddLog(
            "Broadcast : " &
            SendBox.Text)

        Catch ex As Exception

            AddLog(
            ex.Message)

        End Try

    End Sub

    Private Sub ClearLog_Click(
    sender As Object,
    e As RoutedEventArgs)

        LogBox.Items.Clear()

    End Sub


    Private Sub OnMessageReceived(
        sender As Object,
        e As WebSocketMessageEventArgs)

        Dispatcher.Invoke(
            Sub()

                AddLog(
                    $"[{e.Source}] {e.Message}")

            End Sub)

    End Sub

    Private Async Sub Connect_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            Await _ws.Connect(
                UrlBox.Text.Trim())

            AddLog(
                "Connected")

        Catch ex As Exception

            AddLog(
                ex.Message)

        End Try

    End Sub

    Private Async Sub Send_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            Await _ws.SendToServer(
                SendBox.Text)

            AddLog(
                "Send : " &
                SendBox.Text)

        Catch ex As Exception

            AddLog(
                ex.Message)

        End Try

    End Sub

    Private Async Sub Disconnect_Click(
        sender As Object,
        e As RoutedEventArgs)

        Try

            Await _ws.Disconnect()

            AddLog(
                "Disconnected")

        Catch ex As Exception

            AddLog(
                ex.Message)

        End Try

    End Sub

    Private Sub AddLog(
        msg As String)

        LogBox.Items.Add(
            $"{DateTime.Now:HH:mm:ss} {msg}")

    End Sub

End Class