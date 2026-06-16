Imports System.Net.WebSockets
Imports System.Text
Imports System.Threading

Public Class WebSocketClient

    Public Event MessageReceived(
        sender As Object,
        message As String)

    Private _client As ClientWebSocket

    Public Async Function Connect(url As String) As Task

        _client = New ClientWebSocket()

        Await _client.ConnectAsync(
            New Uri(url),
            CancellationToken.None)

        Task.Run(
            Async Function()

                Await ReceiveLoop()

            End Function)

    End Function

    Private Async Function ReceiveLoop() As Task

        Dim buffer(8191) As Byte

        Try

            While _client IsNot Nothing AndAlso
                  _client.State = WebSocketState.Open

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
                        buffer, 0, result.Count)

                RaiseEvent MessageReceived(Me, msg)

            End While

        Catch
        End Try

    End Function

    Public Async Function Send(message As String) As Task

        If _client Is Nothing Then Return
        If _client.State <> WebSocketState.Open Then Return

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

        If _client.State = WebSocketState.Open Then

            Await _client.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "disconnect",
                CancellationToken.None)

        End If

        _client.Dispose()
        _client = Nothing

    End Function

End Class