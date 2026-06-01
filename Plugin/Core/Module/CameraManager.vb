Imports System.Runtime
Imports System.Threading.Tasks
Imports System.Timers
Imports HalconDotNet

'=========================================================
' CameraManager
'
' 相機系統核心管理器
'
' 功能：
' 1. 載入相機插件
' 2. 枚舉所有相機
' 3. 建立相機連線
' 4. 自動重連
' 5. 軟觸發取像
'=========================================================

Public Class CameraManager
    Implements IDisposable

    '=========================================================
    ' 相機字典
    ' key   = 相機名稱
    ' value = 相機插件
    '=========================================================
    Private _cameraMap As New Dictionary(Of String, ICamera)

    '=========================================================
    ' 相機名稱對應序號
    '=========================================================
    Private _cameraNamesMap As New Dictionary(Of String, String)

    '=========================================================
    ' 默認曝光時間
    '=========================================================
    Private _defaultExposure As Single = 10000

    '=========================================================
    ' 重連Timer
    '=========================================================
    Private m_reconnectTimer As Timer

    ' 重試間隔(ms)
    Private ReadOnly m_retryInterval As Integer = 3000

    ' 重試次數
    Private m_retryCount As Integer = 0

    ' 等待重連的相機
    Private _pendingReconnectNames As New List(Of String)

    ' 插件DLL路徑
    Private _pluginDllPath As String


    '=========================================================
    ' Trigger設定
    '=========================================================

    ' 觸發重試次數
    Public Property TriggerRetryCount As Integer = 5

    ' 觸發重試延遲
    Public Property TriggerRetryDelayMs As Integer = 200


    '=========================================================
    ' 曝光時間
    '=========================================================
    Public Property ExposureTime As Single
        Get
            Return _defaultExposure
        End Get
        Set(value As Single)
            _defaultExposure = value
        End Set
    End Property


    '=========================================================
    ' 已連接相機名稱
    '=========================================================
    Public ReadOnly Property CameraNames As List(Of String)
        Get
            Return _cameraMap.Keys.ToList()
        End Get
    End Property


    '=========================================================
    ' 取得指定相機
    '=========================================================
    Public Function GetCamera(name As String) As ICamera

        If _cameraMap.ContainsKey(name) Then
            Return _cameraMap(name)
        End If

        Return Nothing

    End Function


    '=========================================================
    ' 連接所有相機
    '=========================================================
    Public Sub ConnectAllCameras(pluginDllPath As String)

        _pluginDllPath = pluginDllPath

        ' 載入插件
        Dim tempPlugin = PluginManager.LoadPlugin(Of ICamera)(pluginDllPath)

        If tempPlugin Is Nothing Then
            Logger.Error("加载相机插件失败")
            Return
        End If

        Logger.Info("开始枚举相机")

        Dim camNameList As List(Of String) = Nothing

        ' 枚舉相機
        If Not tempPlugin.EnumCamera(camNameList) Then
            Logger.Error("相机枚举失败")
            Return
        End If

        Logger.Info("开始连接相机")

        ' 逐個相機建立連線
        For Each camName In camNameList

            Dim pluginCam = PluginManager.LoadPlugin(Of ICamera)(pluginDllPath)

            If pluginCam Is Nothing Then
                Continue For
            End If

            Dim bRtn = TryConnectCamera(pluginCam, camName)

            If Not bRtn AndAlso Not _pendingReconnectNames.Contains(camName) Then
                _pendingReconnectNames.Add(camName)
            End If

        Next


        ' 若有失敗的相機，啟動自動重連
        If _pendingReconnectNames.Count > 0 Then

            m_reconnectTimer = New Timer(m_retryInterval)

            AddHandler m_reconnectTimer.Elapsed, AddressOf OnHeartbeat

            m_reconnectTimer.AutoReset = True
            m_reconnectTimer.Start()

        End If

    End Sub


    '=========================================================
    ' 心跳重連機制
    '=========================================================
    Private Sub OnHeartbeat(sender As Object, e As ElapsedEventArgs)

        If m_retryCount >= 5 Then

            Logger.Error("连接相机失败，重试次数过多")
            m_reconnectTimer.Stop()

            Return

        End If

        Logger.Info("尝试重新连接相机")

        m_retryCount += 1

        Dim successfulReconnects As New List(Of String)

        For Each camName In _pendingReconnectNames

            Dim pluginCam = PluginManager.LoadPlugin(Of ICamera)(_pluginDllPath)

            If pluginCam Is Nothing Then
                Continue For
            End If

            Dim bRtn = TryConnectCamera(pluginCam, camName)

            If bRtn Then
                successfulReconnects.Add(camName)
            End If

        Next

        For Each camName In successfulReconnects
            _pendingReconnectNames.Remove(camName)
        Next

        If _pendingReconnectNames.Count = 0 Then

            Logger.Info("所有相机连接成功")

            m_reconnectTimer.Stop()

        End If

    End Sub


    '=========================================================
    ' 嘗試連接相機
    '=========================================================
    Private Function TryConnectCamera(plugin As ICamera, camName As String) As Boolean

        Dim err = plugin.Open(camName, Nothing)

        If Not String.IsNullOrEmpty(err) Then

            Logger.Warn($"[{camName}] 打开失败: {err}")
            Return False

        End If


        err = plugin.Start(camName)

        If Not String.IsNullOrEmpty(err) Then

            Logger.Warn($"[{camName}] 启动失败: {err}")
            Return False

        End If


        _cameraMap(camName) = plugin

        Logger.Debug($"[{camName}] 相机连接成功")

        Return True

    End Function


    '=========================================================
    ' 觸發拍照 (核心API)
    '=========================================================
    Public Async Function TriggerImageAsync(
        Optional timeoutMs As Integer = 2000,
        Optional maxRetries As Integer = -1,
        Optional camName As String = ""
    ) As Task(Of HImage)

        If String.IsNullOrEmpty(camName) Then

            If _cameraMap.Count = 0 Then
                Logger.Warn("未连接任何相机")
                Return Nothing
            End If

            camName = _cameraMap.Keys.First()

        End If


        If Not _cameraMap.ContainsKey(camName) Then
            Logger.Warn($"未找到相机 {camName}")
            Return Nothing
        End If


        Dim cam = _cameraMap(camName)

        Dim retries = If(maxRetries >= 0, maxRetries, TriggerRetryCount)

        For attempt = 0 To retries

            If attempt > 0 Then

                Logger.Info($"[{camName}] 触发超时，重试 {attempt}/{retries}")

                Await Task.Delay(TriggerRetryDelayMs)

            End If


            Dim tcs As New TaskCompletionSource(Of CameraFrame)


            ' 接收圖像事件
            Dim handler As Action(Of CameraFrame) =
                Sub(f)

                    If f.CameraName = camName Then

                        RemoveHandler cam.OnFrameGrabbed, handler

                        tcs.TrySetResult(f)

                    End If

                End Sub


            AddHandler cam.OnFrameGrabbed, handler

            cam.SetExposureTime(camName, _defaultExposure)


            Dim err = cam.SoftwareTrigger(camName)

            If Not String.IsNullOrEmpty(err) Then

                RemoveHandler cam.OnFrameGrabbed, handler

                Logger.Warn($"[{camName}] Trigger失败: {err}")

                Continue For

            End If


            Dim completed = Await Task.WhenAny(
                tcs.Task,
                Task.Delay(timeoutMs)
            )


            If completed IsNot tcs.Task Then

                RemoveHandler cam.OnFrameGrabbed, handler

                Logger.Warn($"[{camName}] 触发超时")

                Continue For

            End If


            Return (Await tcs.Task).Image

        Next


        Logger.Error($"[{camName}] 多次触发失败")

        Return Nothing

    End Function


    '=========================================================
    ' 停止所有相機
    '=========================================================
    Public Async Function StopAsync() As Task

        For Each item In _cameraMap

            Await Task.Run(
                Sub()
                    item.Value.Close(item.Key)
                End Sub)

        Next

        _cameraMap.Clear()

    End Function


    '=========================================================
    ' 釋放資源
    '=========================================================
    Public Sub Dispose() Implements IDisposable.Dispose

        StopAsync().Wait()

        For Each cam In _cameraMap.Values
            cam.Dispose()
        Next

    End Sub

End Class