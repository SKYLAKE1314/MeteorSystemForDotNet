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

    ' =====================================================
    ' CORE INITIALIZATION 
    ' =====================================================
    Private Sub InitializeCore()

        AppProgress.Report(10, "載入設定")

        ' =========================
        ' Session（記憶）
        ' =========================
        TemplateSnapshotStore.Load()

        AppProgress.Report(40, "載入模板")

        ' =========================
        ' Template Cache
        ' =========================
        TemplateSnapshotStore.Load()

        TemplateCache.LoadAll()


        AppProgress.Report(70, "初始化相機")

        ' CameraManager.Initialize()  ' 如果你有相機

        AppProgress.Report(100, "完成")

        System.Threading.Thread.Sleep(300)

    End Sub

End Class