Imports System.Net
Imports System.Net.WebSockets
Imports System.Text
Imports System.Threading

Public Class WebSocketManager

    Public Event MessageReceived(
        sender As Object,
        e As WebSocketMessageEventArgs)

    Private _client As ClientWebSocket

    Private _listener As HttpListener

    Private Class ClientItem
        Public Property Id As String
        Public Property Socket As WebSocket
    End Class

    Private ReadOnly _clients As New List(Of ClientItem)

    Private _serverStarted As Boolean


#Region "Server"

    Public Async Function StartServer(ip As String, port As Integer) As Task

        If _serverStarted Then Return
        _serverStarted = True

        _listener = New HttpListener()

        Dim prefix = $"http://{ip}:{port}/ws/"

        _listener.Prefixes.Add(prefix)

        _listener.Start()

        Try

            While _listener IsNot Nothing AndAlso _listener.IsListening

                Dim context = Await _listener.GetContextAsync()

                If Not context.Request.IsWebSocketRequest Then
                    context.Response.StatusCode = 400
                    context.Response.Close()
                    Continue While
                End If

                Dim wsContext = Await context.AcceptWebSocketAsync(Nothing)
                Dim socket = wsContext.WebSocket

                Dim client As New ClientItem With {
    .Id = Guid.NewGuid().ToString(),
    .Socket = socket
}

                SyncLock _clients
                    _clients.Add(client)
                End SyncLock

                RaiseEvent MessageReceived(Me,
    New WebSocketMessageEventArgs("System", $"Client Connected: {client.Id}"))

                Task.Run(Async Function()
                             Await ReceiveServer(client)
                         End Function)

            End While

        Catch ex As HttpListenerException
            ' StopServer 正常觸發

        Catch ex As ObjectDisposedException
            ' 正常關閉

        Finally
            _serverStarted = False
        End Try

    End Function

    Public Sub StopServer()

        Try

            _serverStarted = False

            If _listener IsNot Nothing Then
                _listener.Stop()
                _listener.Close()
                _listener = Nothing
            End If

            Dim clientsCopy As ClientItem()

            SyncLock _clients
                clientsCopy = _clients.ToArray()
                _clients.Clear()
            End SyncLock

            For Each c In clientsCopy

                Try
                    If c.Socket IsNot Nothing AndAlso
                   c.Socket.State = WebSocketState.Open Then

                        c.Socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server Stop",
                        CancellationToken.None)
                    End If

                Catch
                End Try

            Next

        Catch

        End Try

    End Sub

    Private Async Function ReceiveServer(client As ClientItem) As Task

        Dim buffer(8191) As Byte

        Try

            While client.Socket IsNot Nothing AndAlso
              client.Socket.State = WebSocketState.Open

                Dim result = Await client.Socket.ReceiveAsync(
                New ArraySegment(Of Byte)(buffer),
                CancellationToken.None)

                If result.MessageType = WebSocketMessageType.Close Then Exit While

                Dim msg = Encoding.UTF8.GetString(buffer, 0, result.Count)

                RaiseEvent MessageReceived(Me,
                New WebSocketMessageEventArgs("Server:" & client.Id, msg))

            End While

        Catch

        Finally

            SyncLock _clients
                _clients.Remove(client)
            End SyncLock

            Try
                client.Socket.Dispose()
            Catch
            End Try

        End Try

    End Function

    Public Async Function Broadcast(message As String) As Task

        Dim bytes = Encoding.UTF8.GetBytes(message)

        Dim clients As ClientItem()

        SyncLock _clients
            clients = _clients.ToArray()
        End SyncLock

        For Each c In clients

            If c.Socket.State = WebSocketState.Open Then

                Await c.Socket.SendAsync(
                New ArraySegment(Of Byte)(bytes),
                WebSocketMessageType.Text,
                True,
                CancellationToken.None)

            End If

        Next

    End Function

#End Region

#Region "Client"

    Public Async Function Connect(url As String) As Task

        _client = New ClientWebSocket()

        Await _client.ConnectAsync(New Uri(url), CancellationToken.None)

        Task.Run(Async Function()
                     Await ReceiveClient()
                 End Function)

    End Function

    Private Async Function ReceiveClient() As Task

        Dim buffer(8191) As Byte

        Try

            While _client IsNot Nothing AndAlso
                  _client.State =
                  WebSocketState.Open

                Dim result =
                    Await _client.ReceiveAsync(
                        New ArraySegment(Of Byte)(buffer),
                        CancellationToken.None)

                If result.MessageType =
                    WebSocketMessageType.Close Then

                    Exit While

                End If

                Dim msg =
                    Encoding.UTF8.GetString(
                        buffer,
                        0,
                        result.Count)

                RaiseEvent MessageReceived(
                    Me,
                    New WebSocketMessageEventArgs(
                        "Client",
                        msg))

            End While

        Catch
        End Try

    End Function

    Public Async Function SendToServer(
        message As String) As Task

        If _client Is Nothing Then Return

        If _client.State <>
            WebSocketState.Open Then Return

        Dim bytes =
            Encoding.UTF8.GetBytes(message)

        Await _client.SendAsync(
            New ArraySegment(Of Byte)(bytes),
            WebSocketMessageType.Text,
            True,
            CancellationToken.None)

    End Function

    Public Async Function Disconnect() As Task

        If _client Is Nothing Then Return

        If _client.State =
            WebSocketState.Open Then

            Await _client.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Disconnect",
                CancellationToken.None)

        End If

        _client.Dispose()
        _client = Nothing

    End Function

#End Region

End Class