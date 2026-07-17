Imports System.Net.NetworkInformation
Imports System.Threading.Tasks
Imports System.Windows
Imports MetroSystemForDotNet.AppProgress

Class Application

    Private Async Sub Application_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup

        ' ─── 單一執行個體重複啟動防護與 Kill 彈窗 ───
        Try
            Dim currentProc = System.Diagnostics.Process.GetCurrentProcess()
            Dim runningProcs = System.Diagnostics.Process.GetProcessesByName(currentProc.ProcessName)
            Dim otherProc = runningProcs.FirstOrDefault(Function(p) p.Id <> currentProc.Id)

            If otherProc IsNot Nothing Then
                Dim result = MessageBox.Show(
                    "系統檢測到另一個 MeteorSystem 正在運行中。" & vbCrLf & vbCrLf &
                    "是否強制終止（Kill）該執行個體並繼續啟動？" & vbCrLf &
                    "（選擇「否」將退出本次啟動）",
                    "偵測到重複啟動",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning)

                If result = MessageBoxResult.Yes Then
                    otherProc.Kill()
                    otherProc.WaitForExit(3000)
                Else
                    Application.Current.Shutdown()
                    Return
                End If
            End If
        Catch ex As Exception
            Logger.Warn($"[Startup] 重複啟動防護檢測異常: {ex.Message}")
        End Try

        ' 讀取上次儲存的語言
        Dim lang As String = My.Settings.Language

        If String.IsNullOrWhiteSpace(lang) Then
            lang = "zhTW"
        End If

        LanguageManager.Load(lang)
        '' 讀取上次儲存的io
        AppRuntime.IoMode = IoBoardModeHelper.Parse(My.Settings.IoMode)

        Logger.Info($"IO Mode = {AppRuntime.IoMode}")
        ' =========================
        ' Startup UI
        ' =========================
        Dim loading As New Startup()
        loading.Show()

        AppProgress.Report =
            Sub(p, msg)
                loading.UpdateProgress(p, msg)
            End Sub

        ' =========================
        ' Background Init
        ' =========================
        Await Task.Run(Sub()
                           InitializeCore()
                       End Sub)

        ' Open Main UI
        Dim main As New MainWindow()
        main.Show()

        loading.Close()

    End Sub

    Private Sub InitializeCore()

        AppProgress.Report(10, "載入設定")

        TemplateSnapshotStore.Load()
        ' 自動化
        Application.Current.Dispatcher.Invoke(Sub()

                                                  AppRuntime.Process = New ProcessPage()

                                                  ' 把設定丟進去
                                                  AppRuntime.Process.InitializeFromSettings(realtimeEnabled:=My.Settings.RealtimeEnabled)

                                                  If My.Settings.AutoRun Then
                                                      AppRuntime.Process.AutoStartServer()
                                                  End If

                                              End Sub)

        AppProgress.Report(40, "載入模板")

        TemplateSnapshotStore.Load()

        TemplateCache.LoadAll()
        TemplateTrainingStore.WarmupAll()


        AppProgress.Report(60, "初始化相機")

        ' Refresh() 已包含 GetCameras()，不需要再 Initialize()（避免重複枚舉）
        CameraManager.Refresh()

        AppProgress.Report(70, "初始化相機")


        AppProgress.Report(80, "初始化OCR")

        AppRuntime.OCR = New PaddleOcrService()
        AppRuntime.OllamaOCR = New OllamaOcrService()

        ' 於應用程式初始化階段，在背景安全執行 Ollama OCR (gml-ocr) 預先載入，避免檢測時首次載入延遲
        Task.Run(Async Function()
                     Await AppRuntime.OllamaOCR.PreloadModelAsync()
                 End Function)

        AppProgress.Report(90, "初始化Barcode")

        AppRuntime.Barcode = New BarcodeDecodeService()

        AppProgress.Report(100, "稍安勿躁~")

        System.Threading.Thread.Sleep(300)

    End Sub

    Private Sub Application_Exit(sender As Object, e As ExitEventArgs) Handles Me.Exit
        Try
            Logger.Info("[Exit] 應用程式正在結束，釋放所有硬體與連線資源...")
            ' 1. 停止所有相機流
            CameraService.Instance.StopAll()
            ' 2. 停止 WebSocket 伺服器
            If AppRuntime.Process IsNot Nothing Then
                AppRuntime.Process.StopServer()
            End If
        Catch
        End Try
        ' 3. 強制退出處理序，防止背景執行緒殘留
        System.Environment.Exit(0)
    End Sub

End Class

Public Class AppRuntime

    Public Shared OCR As PaddleOcrService
    Public Shared OllamaOCR As OllamaOcrService
    Public Shared Barcode As BarcodeDecodeService
    Public Shared Process As ProcessPage
    Public Shared Home As HomePage

    Public Shared IoMode As IoBoardMode = IoBoardMode.NONE

End Class
