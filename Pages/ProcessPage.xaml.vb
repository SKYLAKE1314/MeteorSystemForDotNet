Imports System.IO
Imports System.Windows
Imports Newtonsoft.Json
Imports System.Linq
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

    Private _endTaskRunning As Boolean = False
    Private _realtimeTimer As System.Timers.Timer

    Public Shared Event OnRealtimeTrigger As Action(Of Action(Of DetectionResult))
    Private _isOnline As Boolean = False

    Private _currentTask As TaskData

    Private _allowDetection As Boolean = False
    Private _detectionResults As New List(Of DetectionResult)()
    Private _currentArtifactFolder As String = ""
    Private _currentTaskStartTime As Long = 0
    Private _recordingInfo As TaskVideoRecorder.RecordingInfo = Nothing

    Private _realtimeRunning As Boolean = False

    Private _isInitializing As Boolean = False

    Public Sub New()
        _isInitializing = True
        InitializeComponent()

        AddHandler _ws.MessageReceived, AddressOf OnMessageReceived
        _router.OnStart = AddressOf StartTask
        _router.OnPause = AddressOf PauseTaskExec
        _router.OnResume = AddressOf ResumeTaskExec
        _router.OnEnd = AddressOf EndTask

        AddHandler Me.Loaded, AddressOf Page_Loaded
    End Sub

    Private Sub Page_Loaded(sender As Object, e As RoutedEventArgs)
        ' Page loaded handler
    End Sub

    Private Sub ClearLog_Click(sender As Object, e As RoutedEventArgs)
        LogBox.Items.Clear()
    End Sub

    Private Sub DataReturn_Checked(sender As Object, e As RoutedEventArgs)
        _mode = RunMode.Mock
    End Sub

    Private Sub DataReturn_Unchecked(sender As Object, e As RoutedEventArgs)
        If _mode = RunMode.Mock Then _mode = RunMode.None
    End Sub

    Private Sub Realtime_Checked(sender As Object, e As RoutedEventArgs)
        If _isInitializing Then Return
        _mode = RunMode.Realtime
        _enableRealtime = True
        TryStartRealtime()
    End Sub

    Private Sub Realtime_Unchecked(sender As Object, e As RoutedEventArgs)
        If _isInitializing Then Return
        If _mode = RunMode.Realtime Then
            _mode = RunMode.None
        End If
        _enableRealtime = False
    End Sub

    Private Sub AddLog(msg As String)
        Dim text = $"{DateTime.Now:HH:mm:ss} {msg}"
        Dispatcher.BeginInvoke(Sub()
                                   LogBox.Items.Add(text)
                               End Sub)
        Logger.Info(text)
    End Sub

    Public Sub InitializeFromSettings(realtimeEnabled As Boolean)
        _isInitializing = True

        ' 只同步設定狀態，不在初始化階段自動觸發檢測
        Realtime.IsChecked = realtimeEnabled
        _enableRealtime = realtimeEnabled
        _mode = If(realtimeEnabled, RunMode.Realtime, RunMode.None)

        AddLog($"[INIT] Realtime={realtimeEnabled}")
        _isInitializing = False
    End Sub

    Private Enum RunMode
        None
        Mock
        Realtime
    End Enum

    Private _mode As RunMode = RunMode.None

End Class
