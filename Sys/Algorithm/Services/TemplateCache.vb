Imports OpenCvSharp
Imports System.IO

Public Class TemplateCache

    Public Shared Templates As New Dictionary(Of String, TemplateData)

    Private Shared _loaded As Boolean = False
    Private Shared ReadOnly _lockObj As New Object()

    ' =========================
    ' Load All Templates
    ' =========================
    Public Shared Sub LoadAll()

        SyncLock _lockObj

            If _loaded Then Return
            _loaded = True

            Templates.Clear()

            Dim root =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates")

            If Not Directory.Exists(root) Then Return

            For Each templateDir In Directory.GetDirectories(root)

                Try

                    Dim imagePath = Path.Combine(templateDir, "template.png")
                    Dim jsonPath = Path.Combine(templateDir, "config.json")

                    If Not File.Exists(imagePath) Then Continue For
                    If Not File.Exists(jsonPath) Then Continue For

                    Dim mat = Cv2.ImRead(imagePath)
                    Dim config = TemplateManager.LoadConfig(jsonPath)

                    Dim name = Path.GetFileName(templateDir)

                    Dim data As New TemplateData With {
                        .Name = name,
                        .Template = mat,
                        .Config = config,
                        .FolderPath = templateDir
                    }

                    Templates(name) = data

                Catch ex As Exception
                    ' 👉 建議至少 log，不然你永遠不知道壞在哪
                    Debug.WriteLine($"[TemplateCache] Load fail: {ex.Message}")
                End Try

            Next

        End SyncLock

    End Sub

    ' =========================
    ' Get
    ' =========================
    Public Shared Function GetTemplate(name As String) As TemplateData

        If String.IsNullOrWhiteSpace(name) Then Return Nothing

        If Templates.ContainsKey(name) Then
            Return Templates(name)
        End If

        Return Nothing

    End Function

End Class