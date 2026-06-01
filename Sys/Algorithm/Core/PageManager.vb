Public Class PageManager

    Public Shared Property HomePage As HomePage

    Public Shared Property AlgorithmPage As AlgorithmPage

    Public Shared Sub Initialize()

        ' =========================
        ' 預加載頁面
        ' =========================
        HomePage = New HomePage()

        AlgorithmPage = New AlgorithmPage()

    End Sub

End Class