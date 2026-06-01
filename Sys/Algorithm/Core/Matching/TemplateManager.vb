Imports OpenCvSharp
Imports Microsoft.Win32
Imports System.Text.Json

Public Class TemplateManager

    Public Class TemplateData

        Public Property Template As Mat

        Public Property Config As TemplateConfig

        Public Property TemplatePath As String

    End Class

    Private Shared ReadOnly TemplateRoot As String =
        IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Templates")

    ' =========================
    ' Ensure Root
    ' =========================
    Private Shared Sub EnsureRoot()

        If Not IO.Directory.Exists(TemplateRoot) Then
            IO.Directory.CreateDirectory(TemplateRoot)
        End If

    End Sub

    ' =========================
    ' Save Template
    ' =========================
    Public Shared Function SaveTemplate(
        template As Mat,
        config As TemplateConfig) As String

        EnsureRoot()

        ' =========================
        ' Folder Name
        ' =========================
        Dim userName As String =
    Microsoft.VisualBasic.InputBox(
        "請輸入模板名稱",
        "保存模板",
        $"Template_{DateTime.Now:yyyyMMdd_HHmmss}"
    )

        If String.IsNullOrWhiteSpace(userName) Then
            Return ""
        End If

        ' 非法字元過濾
        For Each c In IO.Path.GetInvalidFileNameChars()
            userName = userName.Replace(c, "_"c)
        Next

        Dim folderName As String = userName

        Dim folderPath =
            IO.Path.Combine(TemplateRoot, folderName)

        IO.Directory.CreateDirectory(folderPath)

        ' =========================
        ' Save PNG
        ' =========================
        Dim imagePath =
            IO.Path.Combine(folderPath, "template.png")

        Cv2.ImWrite(imagePath, template)

        ' =========================
        ' Save JSON
        ' =========================
        Dim jsonPath =
            IO.Path.Combine(folderPath, "config.json")

        Dim json =
            JsonSerializer.Serialize(
                config,
                New JsonSerializerOptions With {
                    .WriteIndented = True
                })

        IO.File.WriteAllText(jsonPath, json)

        Return folderPath

    End Function

    ' =========================
    ' Load Template
    ' =========================
    Public Shared Function LoadTemplate() As TemplateData

        EnsureRoot()

        Dim dialog As New OpenFileDialog()

        dialog.InitialDirectory = TemplateRoot
        dialog.Filter = "Template Image|template.png"

        If dialog.ShowDialog() <> True Then
            Return Nothing
        End If

        Dim folder =
            IO.Path.GetDirectoryName(dialog.FileName)

        Dim imagePath =
            IO.Path.Combine(folder, "template.png")

        Dim jsonPath =
            IO.Path.Combine(folder, "config.json")

        If Not IO.File.Exists(imagePath) Then
            Return Nothing
        End If

        If Not IO.File.Exists(jsonPath) Then
            Return Nothing
        End If

        ' =========================
        ' Load Template
        ' =========================
        Dim mat =
            Cv2.ImRead(imagePath)

        ' =========================
        ' Load Config
        ' =========================
        Dim json =
            IO.File.ReadAllText(jsonPath)

        Dim config =
            JsonSerializer.Deserialize(Of TemplateConfig)(json)

        Return New TemplateData With {
            .Template = mat,
            .Config = config,
            .TemplatePath = folder
}

    End Function

    ' =========================
    ' Load Config
    ' =========================
    Public Shared Function LoadConfig(
        jsonPath As String) As TemplateConfig

        Try

            If Not IO.File.Exists(jsonPath) Then
                Return Nothing
            End If

            Dim json =
                IO.File.ReadAllText(jsonPath)

            Return JsonSerializer.Deserialize(
                Of TemplateConfig)(json)

        Catch ex As Exception

            Return Nothing

        End Try

    End Function

    ' =========================
    ' Load Template By Folder
    ' =========================
    Public Shared Function LoadTemplate(
        folder As String) As TemplateData

        If String.IsNullOrWhiteSpace(folder) Then
            Return Nothing
        End If

        If Not IO.Directory.Exists(folder) Then
            Return Nothing
        End If

        Dim imagePath =
            IO.Path.Combine(folder, "template.png")

        Dim jsonPath =
            IO.Path.Combine(folder, "config.json")

        If Not IO.File.Exists(imagePath) Then
            Return Nothing
        End If

        If Not IO.File.Exists(jsonPath) Then
            Return Nothing
        End If

        ' =========================
        ' Load Template
        ' =========================
        Dim mat =
            Cv2.ImRead(imagePath)

        ' =========================
        ' Load Config
        ' =========================
        Dim json =
            IO.File.ReadAllText(jsonPath)

        Dim config =
            JsonSerializer.Deserialize(Of TemplateConfig)(json)

        Return New TemplateData With {
    .Template = mat,
    .Config = config,
    .TemplatePath = folder
}

    End Function

End Class