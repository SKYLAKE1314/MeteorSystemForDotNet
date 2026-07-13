Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows
Imports System.Windows.Threading
Imports MetroSystemForDotNet
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Win32
Imports OpenCvSharp
Imports OpenCvSharp.WpfExtensions
Imports Cv = OpenCvSharp

Partial Class HomePage

    Private _initialized As Boolean = False

    Private _currentMat As Mat

    Private _isAlive As Boolean = True
    Private _isActive As Boolean = False
    Private _isStreaming As Boolean = False

    Private _detectLock As New Object()
    Private _isDetecting As Boolean = False
    Private _isPaused As Boolean = False
    Private _isMatchCameraPreWarmed As Boolean = False
    Private Enum DetectionFlowStage
        Idle = 0
        Matching = 1
        Barcode = 2
        Ocr = 3
    End Enum

    Private _flowStage As DetectionFlowStage = DetectionFlowStage.Idle
    Private _skipCurrentStageRequested As Boolean = False
    Private _activeDetectionResult As DetectionResult
    Private _activeDetectionItem As DetectionItem

    Private Const VoicePromptMatchCompleteScan As String = "MatchCompletedPleaseScan.wav"
    Private Const VoicePromptDecodeCompleteOcr As String = "DecodeCompletedPleaseOCR.wav"
    Private Const VoicePromptSingleFlowCompleted As String = "SingleFlowCompleted.wav"
    Private Const VoicePromptStageTimeout As String = "StageTimeout.wav"                    ' 階段超時
    Private Const VoicePromptStageSkipped As String = "StageSkipped.wav"                    ' 階段已跳過
    Private Const VoicePromptStageRecover As String = "StageRecover.wav"                    ' 收到信號 2：恢復錄製與檢測
    Private Const VoicePromptDetectionPaused As String = "DetectionPaused.wav"              ' 收到信號 1：暫停錄製與檢測
    Private Const VoicePromptDetectionReady As String = "DetectionReady.wav"                ' 收到信號 0：相機就緒，可以開始檢測
    Private Const VoicePromptCorrect As String = "Correct.wav"                              ' 正確（解碼/OCR 成功）
    Private Const VoicePromptError As String = "NoTemplate.wav"
    Private Const VoicePromptNoTemplate As String = "Error.wav"                             ' 找不到供應商對應模板時播報
    Private Const StageTimeoutMs As Integer = 60000
    Private Const StageLoopDelayMs As Integer = 30

    ' 優先使用設定中儲存的相機，否則使用第一個相機
    'Private _detectCameraId As String = If(String.IsNullOrWhiteSpace(My.Settings.CameraDeviceId), GetCamId(0), My.Settings.CameraDeviceId)
    'Private _ocrCameraId As String = If(String.IsNullOrWhiteSpace(My.Settings.CameraDeviceId), GetCamId(0), My.Settings.CameraDeviceId)

    Private _io As IOController

    Private _lastFrameMat As Mat
    Private _lastFrameBitmap As BitmapSource

    Private ReadOnly _ocr As PaddleOcrService =
    AppRuntime.OCR

    ' Page Loaded
    Private Async Sub Page_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        AddHandler LanguageManager.LanguageChanged, AddressOf RefreshLanguageUI

        If _initialized Then
            ' 如果是重複返回此頁面，重新註冊 UI 刷新循環，避免畫面凍結
            RemoveHandler CompositionTarget.Rendering, AddressOf UpdateFrame
            AddHandler CompositionTarget.Rendering, AddressOf UpdateFrame
            Return
        End If

        _initialized = True

        ' 顯示載入遮罩
        HomeLoadingOverlay.Visibility = Visibility.Visible
        Await Task.Yield() ' 讓 UI 先渲染遮罩

        Try
            Logger.SetWpfRichTextBox(rtbLog)

            ' IO 初始化
            _io = New IOController(
                "192.168.1.117",
                502,
                1,
                0,
                AppRuntime.IoMode,
                Sub(msg) Logger.Info(msg)
            )
            Await _io.InitializeAsync()

            If AppRuntime.IoMode = IoBoardMode.NONE Then
                Logger.Info("IO 已停用，保留語音播報")
            Else
                Logger.Info("IO 初始化完成")
                _io.SetLightYellow()  ' 程式載入後黃燈常亮待機
            End If

            AddHandler ProcessPage.OnRealtimeTrigger, AddressOf RunDetection
            AddHandler Logger.LogReceived, AddressOf GlobalLogReceived

            Try
                _io.StartDIListener(0)
                AddHandler _io.ButtonChanged, AddressOf IoButtonChanged
                Logger.Info("物理按鈕監聽已啟用")
            Catch ex As Exception
                Logger.Error("啟用物理按鈕監聽失敗: " & ex.Message)
            End Try

            ' 顯示與設定頁相同的完整相機列表，並精確分配相機 1 與相機 2 的 ID
            RefreshCameraComboBox()

            ' 訂閱相機變更事件：設定頁儲存相機後立即刷新本頁的相機清單
            RemoveHandler CameraManager.CameraChanged, AddressOf OnCameraChanged
            AddHandler CameraManager.CameraChanged, AddressOf OnCameraChanged

            ' 【核心修復】註冊 WPF 渲染引擎回調，每當 UI 準備繪製下一幀時，自動執行 UpdateFrame
            RemoveHandler CompositionTarget.Rendering, AddressOf UpdateFrame
            AddHandler CompositionTarget.Rendering, AddressOf UpdateFrame
            _isStreaming = True

        Catch ex As Exception
            Logger.Error($"HomePage 初始化失敗: {ex.Message}")
        Finally
            HomeLoadingOverlay.Visibility = Visibility.Collapsed
            Logger.Info("HomePage 已載入並啟用高幀率實時渲染循環")
        End Try
    End Sub

    Private Sub Page_Unloaded(sender As Object, e As RoutedEventArgs) Handles Me.Unloaded
        ' 【核心修復】離開頁面時必須解除註冊，防止背景線程持續索取已關閉的硬體資源
        RemoveHandler CompositionTarget.Rendering, AddressOf UpdateFrame
        RemoveHandler CameraManager.CameraChanged, AddressOf OnCameraChanged
        _isStreaming = False
    End Sub
    Public Sub RefreshLanguageUI()

        BtnLoadImage.Content = LanguageManager.T("Home_BtnLoadImage")
        BtnClear.Content = LanguageManager.T("Home_BtnClear")
        BtnStart.Content = LanguageManager.T("Home_BtnStart")
        BtnStop.Content = LanguageManager.T("Home_BtnStop")
        BtnGetImg.Content = LanguageManager.T("Home_BtnGetImg")
        BtnSave.Content = LanguageManager.T("Home_BtnSave")
        BtnLaplacian.Content = LanguageManager.T("Home_BtnLaplacian")

    End Sub
    Private Sub GlobalLogReceived(level As String, msg As String)

        Dispatcher.Invoke(Sub()

                              ' 這裡你可以：
                              ' 1. 更新本頁 log
                              ' 2. 或丟到共享 log window

                              rtbLog.AppendText($"[{level}] {msg}" & Environment.NewLine)
                              rtbLog.ScrollToEnd()

                          End Sub)

    End Sub
    ' =========================================
    ' Show Render
    ' =========================================
    ' =========================================
    ' Show Render (修正版)
    ' =========================================
    Public Async Sub ShowRender(mat As Mat)
        ' 【防禦 1】基本有效性檢查
        If mat Is Nothing OrElse mat.IsDisposed OrElse mat.CvPtr = IntPtr.Zero Then Return

        ' 如果當前不是定位匹配階段，直接顯示原生畫面
        If _flowStage <> DetectionFlowStage.Matching Then
            RenderImage.Source = mat.ToWriteableBitmap()
            Return
        End If

        ' =========================
        ' Template Name & Cache Check
        ' =========================
        Dim templatePath = LastTemplateStore.Load()
        If String.IsNullOrWhiteSpace(templatePath) Then
            RenderImage.Source = mat.ToWriteableBitmap()
            Return
        End If

        Dim templateName = IO.Path.GetFileName(templatePath)
        Dim data = TemplateCache.GetTemplate(templateName)

        If data Is Nothing Then
            RenderImage.Source = mat.ToWriteableBitmap()
            Return
        End If

        ' =================================================================
        ' 【核心修復】主線程立刻 Clone，與全域 _lastFrameMat 徹底切斷生命週期關係！
        ' =================================================================
        ' 不要在 MatchAsync 內部才 Clone！必須在當前主線程立刻複製一份。
        ' 這樣不論 UpdateFrame 接下來怎麼 Dispose 或更新 _lastFrameMat，背景 Task 都不會受到干擾。
        Dim matCopyForMatch As Mat = Nothing
        Try
            matCopyForMatch = mat.Clone()
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"[Render] 主線程 Clone 失敗: {ex.Message}")
            Return
        End Try

        Try
            ' 將這個絕對安全的獨立副本送進去運算
            Dim result = Await TemplateMatcher.MatchAsync(
                matCopyForMatch,
                data.Template,
                data.Config.Threshold,
                data.Config.MatchMethod)

            ' 【核心釋放】當 Await 結束，不論成功與否，背景運算都結束了，必須立刻釋放此副本
            If matCopyForMatch IsNot Nothing AndAlso Not matCopyForMatch.IsDisposed Then
                matCopyForMatch.Dispose()
                matCopyForMatch = Nothing
            End If

            If result Is Nothing Then Return

            ' 【防禦 2】二次檢查：異步運算回傳時，確認是否還在 Matching 階段
            If _flowStage <> DetectionFlowStage.Matching Then
                ' 如果階段已經變了，直接把結果影像銷毀，拒絕渲染舊框
                If result.ResultImage IsNot Nothing AndAlso Not result.ResultImage.IsDisposed Then
                    result.ResultImage.Dispose()
                End If
                Return
            End If

            ' =========================
            ' Render Overlay (畫框 + 分數)
            ' =========================
            If result.ResultImage IsNot Nothing AndAlso Not result.ResultImage.IsDisposed AndAlso result.ResultImage.CvPtr <> IntPtr.Zero Then
                Using display = result.ResultImage.Clone()
                    Dim text As String = $"Score: {result.Score:F3} (Stage: Matching)"
                    Dim org As New OpenCvSharp.Point(20, 40)

                    Cv2.PutText(
                        display,
                        text,
                        org,
                        HersheyFonts.HersheySimplex,
                        1.0,
                        If(result.IsOk, Scalar.Lime, Scalar.Yellow),
                        2)

                    RenderImage.Source = display.ToWriteableBitmap()
                End Using

                ' 記得釋放 result 內帶出來的原圖，避免內存洩漏
                result.ResultImage.Dispose()
            End If

        Catch ex As ObjectDisposedException
            ' 安靜跳過時間差釋放異常
        Catch ex As Exception
            Logger.Error($"ShowRender error: {ex.Message}")
        Finally
            ' 雙重保險：確保副本一定被釋放
            If matCopyForMatch IsNot Nothing AndAlso Not matCopyForMatch.IsDisposed Then
                matCopyForMatch.Dispose()
            End If
        End Try
    End Sub
    ' 實時畫面流回調
    ' =================================================================
    ' 【核心修復】實時畫面流回調（同步更新 Mat 快取，解決匹配錯亂與卡死）
    ' =================================================================
    Private Sub UpdateFrame(sender As Object, e As EventArgs)
        ' 1. 根據目前的流程階段，決定從哪台相機拿硬體畫面快取
        Dim targetCamId As String = _matchCameraId

        If _flowStage = DetectionFlowStage.Barcode OrElse _flowStage = DetectionFlowStage.Ocr Then
            targetCamId = _ocrCameraId
        End If

        If String.IsNullOrWhiteSpace(targetCamId) Then Return

        Try
            ' 2. 從正確的相機通道獲取影像 (BitmapSource)
            Dim frame = CameraService.Instance.GetFrame(targetCamId)
            If frame Is Nothing Then Return

            ' 3. 分流渲染與快取同步邏輯
            If _flowStage = DetectionFlowStage.Matching Then
                ' 【關鍵修正】在進入 ShowRender 之前，必須將當前相機通路的最新畫面轉成 Mat 並更新全域暫存
                ' 這樣 ShowRender 拿到的 _lastFrameMat 才是「活的、當下的實時畫面」，而不是歷史殘留畫面
                Dim oldMat = _lastFrameMat

                ' 即時將 BitmapSource 轉為 OpenCV 的 Mat 矩陣
                _lastFrameMat = frame.ToMat()

                ' 安全釋放上一幀的 Mat 記憶體，防止託管與非託管記憶體洩漏 (Memory Leak)
                If oldMat IsNot Nothing AndAlso Not oldMat.IsDisposed Then
                    oldMat.Dispose()
                End If

                ' 呼叫匹配與渲染
                If _lastFrameMat IsNot Nothing AndAlso Not _lastFrameMat.IsDisposed Then
                    ShowRender(_lastFrameMat)
                End If
            Else
                ' 非匹配定位階段（條碼、OCR 階段）：直接顯示原生相機畫面，不畫定位框，不跑 Match 演算法
                RenderImage.Source = frame
            End If

        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"UpdateFrame 同步與轉換發生異常: {ex.Message}")
        End Try
    End Sub
    Private Sub BtnClearLog_Click(sender As Object, e As RoutedEventArgs)
        ' 將 Document 設為新的 FlowDocument 即達成了清空效果
        rtbLog.Document = New FlowDocument()
    End Sub
    ''' <summary>
    ''' 從 CameraManager 快取重新載入相機清單並更新 ComboBox，
    ''' 盡量保留使用者當前的選擇（若該相機仍存在於清單中）。
    ''' 必須在 UI 執行緒呼叫。
    ''' </summary>
    ' 變更為精確對應：相機 1 (定位)、相機 2 (OCR/解碼)
    Private _matchCameraId As String = ""
    Private _ocrCameraId As String = ""

    Private Sub RefreshCameraComboBox()
        Dim allCameras = CameraManager.GetCachedCameras()
        If allCameras Is Nothing OrElse allCameras.Count = 0 Then Return

        CameraComboBox.ItemsSource = allCameras

        ' ─── 精確對接 SettingPage 儲存的多相機陣列 ───
        Dim savedIds = My.Settings.CameraDeviceIds

        If savedIds IsNot Nothing AndAlso savedIds.Count > 0 Then
            ' 相機 1 用於定位匹配
            _matchCameraId = savedIds(0)

            ' 相機 2 用於條碼與 OCR（如果使用者有設定第二台的話，否則 fallback 到第一台）
            If savedIds.Count > 1 Then
                _ocrCameraId = savedIds(1)
            Else
                _ocrCameraId = savedIds(0)
            End If
        Else
            ' Fallback 機制
            _matchCameraId = allCameras(0).DeviceId
            _ocrCameraId = allCameras(0).DeviceId
        End If

        ' 實時檢測界面上的 ComboBox 顯示目前正在工作的相機（依階段決定）
        Dim currentWorkingId = If(_flowStage = DetectionFlowStage.Ocr OrElse _flowStage = DetectionFlowStage.Barcode, _ocrCameraId, _matchCameraId)
        Dim toSelect = allCameras.FirstOrDefault(Function(c) String.Equals(c.DeviceId, currentWorkingId, StringComparison.OrdinalIgnoreCase))

        CameraComboBox.SelectedItem = toSelect
        Logger.Info($"[Camera] 實時頁面相機載入成功。定位相機：{_matchCameraId}，OCR相機：{_ocrCameraId}")
    End Sub

    ''' <summary>
    ''' 設定頁儲存相機設定後觸發（CameraManager.CameraChanged），
    ''' 在 UI 執行緒重新整理相機清單，讓變更立即生效，不需要重開此頁。
    ''' </summary>
    ' 設定頁變更後的動態套用
    Private Sub OnCameraChanged()
        Dispatcher.Invoke(Sub()
                              Try
                                  RefreshCameraComboBox() ' 更新本頁的 _matchCameraId 與 _ocrCameraId 變數
                              Catch ex As Exception
                                  Logger.Error($"[Camera] 刷新相機清單失敗: {ex.Message}")
                              End Try
                          End Sub)

        Task.Run(Sub()
                     Dim ids = My.Settings.CameraDeviceIds
                     If ids Is Nothing OrElse ids.Count = 0 Then
                         CameraService.Instance.StopAll()
                         Return
                     End If

                     ' 先關閉所有相機，乾淨釋放硬體資源
                     CameraService.Instance.StopAll()
                     System.Threading.Thread.Sleep(300)

                     ' 重新啟動相機 1 (定位)
                     Dim cam1 = ids(0)
                     If Not String.IsNullOrWhiteSpace(cam1) Then
                         CameraService.Instance.StartCamera(cam1)
                         Logger.Info($"[Camera] 定位相機已啟動: {cam1}")
                     End If

                     ' 重新啟動相機 2 (OCR/解碼)（如果存在且與相機 1 不同設備）
                     If ids.Count > 1 Then
                         Dim cam2 = ids(1)
                         If Not String.IsNullOrWhiteSpace(cam2) Then
                             CameraService.Instance.StartCamera(cam2)
                             Logger.Info($"[Camera] OCR/解碼相機已啟動: {cam2}")
                         End If
                     End If
                 End Sub)
    End Sub
End Class
