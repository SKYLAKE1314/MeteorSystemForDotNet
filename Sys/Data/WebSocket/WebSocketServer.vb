Imports System.Net
Imports System.Net.WebSockets
Imports System.Text
Imports System.Threading

Public Class WebSocketServer

    ' =========================
    ' Event（統一 EventArgs）
    ' =========================
    Public Event MessageReceived(
        sender As Object,
        e As WebSocketMessageEventArgs)

    ' =========================
    ' Private Fields
    ' =========================
    Private _listener As HttpListener
    Private _running As Boolean

    Private ReadOnly _clients As New Dictionary(Of String, WebSocket)

    ' =========================
    ' Start Server
    ' =========================
    Public Async Function StartServer(port As Integer) As Task

        If _running Then Return
        _running = True

        _listener = New HttpListener()

        ' ✔ 監聽所有網卡（關鍵）
        _listener.Prefixes.Add($"http://+:{port}/ws/")

        Try
            _listener.Start()

            RaiseEvent MessageReceived(
                Me,
                New WebSocketMessageEventArgs(
                    "System",
                    $"Server Started : {port}"))

        Catch ex As Exception
            RaiseEvent MessageReceived(
                Me,
                New WebSocketMessageEventArgs(
                    "Error",
                    ex.ToString()))
            Return
        End Try

        ' =========================
        ' Accept Loop
        ' =========================
        While _running

            Dim context = Await _listener.GetContextAsync()

            If Not context.Request.IsWebSocketRequest Then
                context.Response.StatusCode = 400
                context.Response.Close()
                Continue While
            End If

            Dim wsContext = Await context.AcceptWebSocketAsync(Nothing)
            Dim socket = wsContext.WebSocket

            Dim id = Guid.NewGuid().ToString()

            SyncLock _clients
                _clients.Add(id, socket)
            End SyncLock

            RaiseEvent MessageReceived(
                Me,
                New WebSocketMessageEventArgs(
                    "System",
                    $"Client Connected: {id}"))

            ' 開收訊息 thread
            Task.Run(
                Async Function()
                    Await ReceiveLoop(id, socket)
                End Function)

        End While

    End Function

    ' =========================
    ' Receive Loop
    ' =========================
    Private Async Function ReceiveLoop(
        clientId As String,
        socket As WebSocket) As Task

        Dim buffer(8191) As Byte

        Try

            While socket IsNot Nothing AndAlso
                  socket.State = WebSocketState.Open

                Dim result = Await socket.ReceiveAsync(
                    New ArraySegment(Of Byte)(buffer),
                    CancellationToken.None)

                If result.MessageType = WebSocketMessageType.Close Then Exit While

                Dim msg = Encoding.UTF8.GetString(buffer, 0, result.Count)

                RaiseEvent MessageReceived(
                    Me,
                    New WebSocketMessageEventArgs(
                        clientId,
                        msg))

            End While

        Catch ex As Exception

        Finally

            SyncLock _clients
                _clients.Remove(clientId)
            End SyncLock

            Try
                socket?.Abort()
                socket?.Dispose()
            Catch
            End Try

            RaiseEvent MessageReceived(
                Me,
                New WebSocketMessageEventArgs(
                    "System",
                    $"Client Disconnected: {clientId}"))

        End Try

    End Function

    ' =========================
    ' Broadcast
    ' =========================
    Public Async Function Broadcast(message As String) As Task

        Dim bytes = Encoding.UTF8.GetBytes(message)

        Dim clients As WebSocket()

        SyncLock _clients
            clients = _clients.Values.ToArray()
        End SyncLock

        For Each socket In clients

            Try
                If socket.State = WebSocketState.Open Then

                    Await socket.SendAsync(
                        New ArraySegment(Of Byte)(bytes),
                        WebSocketMessageType.Text,
                        True,
                        CancellationToken.None)

                End If
            Catch
            End Try

        Next

    End Function

    ' =========================
    ' Stop Server
    ' =========================
    Public Sub [Stop]()

        _running = False

        Try
            _listener?.Stop()
            _listener?.Close()
        Catch
        End Try

        Dim clients As WebSocket()

        SyncLock _clients
            clients = _clients.Values.ToArray()
            _clients.Clear()
        End SyncLock

        For Each socket In clients
            Try
                socket.Abort()
                socket.Dispose()
            Catch
            End Try
        Next

    End Sub

End Class