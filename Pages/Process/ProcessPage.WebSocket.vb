Imports System.Windows
Imports Newtonsoft.Json
Imports VAT.Common
Imports VAT.Common.VATJsonObject

Partial Public Class ProcessPage

    Private _serverStarted As Boolean = False

    Private Sub StartServer_Click(sender As Object, e As RoutedEventArgs)
        Try
            If _serverStarted Then
                AddLog("Server 已啟動")
                Return
            End If

            Dim port As Integer = Integer.Parse(PortBox.Text)
            AddLog($"Server Started : 0.0.0.0:{port}")

            Task.Run(Async Function()
                         Await _ws.StartServer(port)
                     End Function)

            _serverStarted = True
        Catch ex As Exception
            AddLog(ex.Message)
        End Try
    End Sub

    Public Async Sub AutoStartServer()
        If _serverStarted Then Return
        Try
            Dim port As Integer = Integer.Parse(PortBox.Text)
            Await _ws.StartServer(port)
            _serverStarted = True
            AddLog("Auto Server Started")
        Catch ex As Exception
            AddLog(ex.Message)
        End Try
    End Sub

    Private Sub StopServer_Click(sender As Object, e As RoutedEventArgs)
        Try
            _ws.StopServer()
            _serverStarted = False
            AddLog("Server Stopped")
        Catch ex As Exception
            AddLog(ex.Message)
        End Try
    End Sub

    Public Sub StopServer()
        Try
            _ws.StopServer()
            _serverStarted = False
        Catch
        End Try
    End Sub

    Private Async Sub Broadcast_Click(sender As Object, e As RoutedEventArgs)
        Try
            Await _ws.Broadcast(SendBox.Text)
            AddLog("Broadcast : " & SendBox.Text)
        Catch ex As Exception
            AddLog(ex.Message)
        End Try
    End Sub

    Private Sub OnMessageReceived(sender As Object, e As WebSocketMessageEventArgs)
        Try
            Dim rawMessage = If(e.Message, "").Trim()

            If String.Equals(e.Source, "System", StringComparison.OrdinalIgnoreCase) Then
                AddLog($"[WS] {rawMessage}")
                Exit Sub
            End If

            If String.IsNullOrWhiteSpace(rawMessage) Then Exit Sub

            ' 只有 JSON 才進入 router，避免連線/提示訊息被誤判成 taskStatus=0
            If Not rawMessage.StartsWith("{") AndAlso Not rawMessage.StartsWith("[") Then
                AddLog($"[WS:{e.Source}] {rawMessage}")
                Exit Sub
            End If

            Dim msg As New VATJsonObject(rawMessage)
            If msg Is Nothing Then Exit Sub

            Task.Run(Sub()
                         Try
                             _router.Route(msg)
                         Catch ex As Exception
                             Dispatcher.BeginInvoke(Sub() MeteorMessageBox.ShowError("Router Error: " & ex.Message))
                         End Try
                     End Sub)
        Catch ex As Exception
            Dispatcher.BeginInvoke(Sub() MeteorMessageBox.ShowError($"Parse Error [{e.Source}] {ex.Message}"))
        End Try
        Logger.Info("Receive : " & Me.GetHashCode())
    End Sub

    Private Async Sub Connect_Click(sender As Object, e As RoutedEventArgs)
        Try
            Await _ws.Connect(UrlBox.Text.Trim())
            AddLog("Connected")
        Catch ex As Exception
            AddLog(ex.Message)
        End Try
    End Sub

    Private Async Sub Send_Click(sender As Object, e As RoutedEventArgs)
        Try
            Await _ws.SendToServer(SendBox.Text)
            AddLog("Send : " & SendBox.Text)
        Catch ex As Exception
            AddLog(ex.Message)
        End Try
    End Sub

    Private Async Sub Disconnect_Click(sender As Object, e As RoutedEventArgs)
        Try
            Await _ws.Disconnect()
            AddLog("Disconnected")
        Catch ex As Exception
            AddLog(ex.Message)
        End Try
    End Sub

End Class
