Imports OpenCvSharp
Imports System.IO

Public Class TemplateCache

    Public Shared Templates As New Dictionary(Of String, TemplateData)

    Private Shared _loaded As Boolean = False
    Private Shared ReadOnly _lockObj As New Object()

    ' =========================
    ' Reload (force refresh)
    ' =========================
    Public Shared Sub Reload()
        SyncLock _lockObj
            _loaded = False
        End SyncLock
        LoadAll()
    End Sub

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

                    ' Old style: template.png directly in group folder
                    Dim imagePath = Path.Combine(templateDir, "template.png")
                    Dim jsonPath = Path.Combine(templateDir, "config.json")

                    If File.Exists(imagePath) AndAlso File.Exists(jsonPath) Then
                        Dim mat = Cv2.ImRead(imagePath)
                        Dim config = TemplateManager.LoadConfig(jsonPath)
                        Dim name = Path.GetFileName(templateDir)
                        Templates(name) = New TemplateData With {
                            .Name = name,
                            .Template = mat,
                            .Config = config,
                            .FolderPath = templateDir
                        }
                    End If

                    ' New style: cam* subdirectories within group folder
                    For Each camDir In Directory.GetDirectories(templateDir)
                        Try
                            Dim camImage = Path.Combine(camDir, "template.png")
                            Dim camJson = Path.Combine(camDir, "config.json")
                            If Not File.Exists(camImage) Then Continue For
                            If Not File.Exists(camJson) Then Continue For
                            Dim camMat = Cv2.ImRead(camImage)
                            Dim camConfig = TemplateManager.LoadConfig(camJson)
                            Dim camName = Path.GetFileName(camDir)
                            Templates(camName) = New TemplateData With {
                                .Name = camName,
                                .Template = camMat,
                                .Config = camConfig,
                                .FolderPath = camDir
                            }
                        Catch ex As Exception
                            Debug.WriteLine($"[TemplateCache] cam subdir load fail: {ex.Message}")
                        End Try
                    Next

                Catch ex As Exception
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