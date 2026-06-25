Imports System.Reflection.Metadata
Imports System.Windows
Imports VAT.Common
Imports VAT.Common.VATJsonObject

Partial Public Class ProcessPage

    Private ReadOnly _ws As New WebSocketManager()
    Private ReadOnly _server As New WebSocketServer()

    Private ReadOnly _client As New WebSocketClient()
    Private ReadOnly _router As New TaskRouter()

    Private _taskMap As New Dictionary(Of String, VATJsonObject)

    Private _enableDataReturn As Boolean = False
    Private _enableRealtime As Boolean = False

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
        _router.OnStart = AddressOf StartTask
        _router.OnPause = AddressOf PauseTask
        _router.OnResume = AddressOf ResumeTask
        _router.OnEnd = AddressOf EndTask


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

            Dim port As Integer =
            Integer.Parse(PortBox.Text)

            AddLog($"Server Started : 0.0.0.0:{port}")

            Task.Run(
            Async Function()

                Await _ws.StartServer(port)

            End Function)

            _serverStarted = True

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
    Private Sub OnMessageReceived(sender As Object, e As WebSocketMessageEventArgs)

        Dispatcher.Invoke(Sub()

                              Try
                                  Dim msg As New VATJsonObject(e.Message)

                                  Try
                                      If msg Is Nothing Then Exit Sub
                                      _router.Route(msg)
                                  Catch ex As Exception
                                      ErrorDialogHelper.ShowError("Router Error: " & ex.Message)
                                  End Try

                              Catch ex As Exception
                                  ErrorDialogHelper.ShowError($"Parse Error [{e.Source}] {ex.Message}")
                              End Try

                          End Sub)

    End Sub

    Private Sub StartTask(t As TaskData)

        AddLog($"START {t.RequestId}")

        AddLog($"Part={t.PartCode}, Supplier={t.SupplierCode}, Count={t.PartCount}")

        AddLog($"Station={t.StationId}")

    End Sub

    Private Sub LogTaskStart(t As TaskData)

        AddLog($"START {t.RequestId}")
        AddLog($"Part={t.PartCode}, Supplier={t.SupplierCode}, Count={t.PartCount}")
        AddLog($"Station={t.StationId}")

    End Sub

    Private Sub PauseTask(t As TaskData)
        AddLog("PAUSE: " & t.RequestId)
    End Sub

    Private Sub ResumeTask(t As TaskData)
        AddLog("RESUME: " & t.RequestId)
    End Sub
    Private Async Sub EndTask(t As TaskData)

        AddLog("END: " & t.RequestId)

        '如果有勾選「數據返回」
        If _enableDataReturn Then

            Dim result As New VATJsonObject()

            result("requestId") = t.RequestId
            result("taskStatus") = 3
            result("partCode") = t.PartCode
            result("supplierCode") = t.SupplierCode
            result("partCount") = t.PartCount
            result("stationId") = t.StationId

            result("resultTime") = DateTime.Now.ToString("HH:mm:ss")

            Await _ws.SendToServer(result.ToString())

            AddLog("RESULT RETURNED")

        End If

    End Sub

    Private Sub HandleLog(msg As VATJsonObject)
        AddLog("[LOG] " & msg("msg").ToString())
    End Sub

    Private Sub HandleData(msg As VATJsonObject)

        Dim cam As String = msg("camera")
        Dim score As Double = Double.Parse(msg("score"))

        AddLog($"Camera={cam}, Score={score}")

    End Sub

    Private Sub HandleStatus(msg As VATJsonObject)

        Dim device = msg("device")
        Dim online = Boolean.Parse(msg("online"))

        AddLog($"{device} Online={online}")

    End Sub

    ' 數據交互

    Private Sub DataReturn_Checked(sender As Object, e As RoutedEventArgs)
        _enableDataReturn = True
    End Sub

    Private Sub DataReturn_Unchecked(sender As Object, e As RoutedEventArgs)
        _enableDataReturn = False
    End Sub

    Private Sub Realtime_Checked(sender As Object, e As RoutedEventArgs)
        _enableRealtime = True
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