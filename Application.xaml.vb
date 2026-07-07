Imports System.Net.NetworkInformation
Imports System.Threading.Tasks
Imports System.Windows
Imports MetroSystemForDotNet.AppProgress

Class Application

    Private Async Sub Application_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup

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

        ' =========================
        ' Open Main UI
        ' =========================
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

        AppProgress.Report(90, "初始化Barcode")

        AppRuntime.Barcode = New BarcodeDecodeService()

        AppProgress.Report(100, "稍安勿躁~")

        System.Threading.Thread.Sleep(300)

    End Sub

End Class

Public Class AppRuntime

    Public Shared OCR As PaddleOcrService
    Public Shared Barcode As BarcodeDecodeService
    Public Shared Process As ProcessPage
    Public Shared Home As HomePage

    Public Shared IoMode As IoBoardMode = IoBoardMode.NONE

End Class
