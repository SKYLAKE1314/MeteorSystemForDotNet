Imports OpenCvSharp
Imports System.Text.Json
Imports System.Linq

Public Class TemplateTrainingStore

    Private Shared ReadOnly _cacheLock As New Object()
    Private Shared ReadOnly _sampleMetaCache As New Dictionary(Of String, List(Of TrainingSampleMeta))(StringComparer.OrdinalIgnoreCase)
    Private Shared ReadOnly _sampleBytesCache As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)

    Public Class TrainingTemplateParams
        Public Property MasterThreshold As Double = 0.8
        Public Property PyramidLevel As Integer = 2
        Public Property MatchMethod As Integer = 0
        Public Property MinArea As Integer = 50
        Public Property CannyLow As Integer = 80
        Public Property CannyHigh As Integer = 160
        Public Property AngleMin As Double = -60
        Public Property AngleMax As Double = 60
        Public Property AngleStep As Double = 3
        Public Property MaxSamples As Integer = 50
    End Class

    Public Class TrainingPoint
        Public Property X As Integer
        Public Property Y As Integer
    End Class

    Public Class TrainingSampleMeta
        Public Property FileName As String
        Public Property CreatedAt As Long
        Public Property LastMatchedAt As Long
        Public Property RoiX As Integer
        Public Property RoiY As Integer
        Public Property RoiW As Integer
        Public Property RoiH As Integer
        Public Property MasterThreshold As Double
        Public Property PyramidLevel As Integer
        Public Property MatchMethod As Integer
        Public Property MinArea As Integer
        Public Property CannyLow As Integer
        Public Property CannyHigh As Integer
        Public Property AngleMin As Double
        Public Property AngleMax As Double
        Public Property AngleStep As Double
        Public Property PolygonPoints As List(Of TrainingPoint) = New List(Of TrainingPoint)()
    End Class

    Private Class TrainingIndexFile
        Public Property Samples As List(Of TrainingSampleMeta) = New List(Of TrainingSampleMeta)()
    End Class

    Public Shared Function NormalizeGroupPath(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Return ""
        If Not IO.Directory.Exists(path) Then Return ""

        Dim name = IO.Path.GetFileName(path)
        If name IsNot Nothing AndAlso name.StartsWith("cam", StringComparison.OrdinalIgnoreCase) Then
            Dim parent = IO.Directory.GetParent(path)
            If parent IsNot Nothing AndAlso parent.Exists Then
                Return parent.FullName
            End If
        End If

        Return path
    End Function

    Public Shared Sub InvalidateCache(groupPath As String)
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return
        SyncLock _cacheLock
            _sampleMetaCache.Remove(groupRoot)
            ' 移除該 group 下所有 sample 的圖片快取
            Dim keysToRemove = _sampleBytesCache.Keys.
                Where(Function(k) k.StartsWith(groupRoot, StringComparison.OrdinalIgnoreCase)).
                ToList()
            For Each k In keysToRemove
                _sampleBytesCache.Remove(k)
            Next
        End SyncLock
        Logger.Debug($"[TemplateTraining] 快取已清除: {groupRoot}")
    End Sub

    Public Shared Function GetTrainingSampleCount(groupPath As String) As Integer
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return 0

        Dim idx = LoadIndex(groupRoot)
        Return idx.Samples.Count
    End Function

    Public Shared Function AddSamplePolygon(groupPath As String,
                                            source As Mat,
                                            polygon As List(Of Point),
                                            params As TrainingTemplateParams) As Integer

        If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
        If polygon Is Nothing OrElse polygon.Count < 3 Then Throw New ArgumentException("Polygon ROI invalid")

        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Throw New IO.DirectoryNotFoundException("Template group not found")

        Cv2.SetUseOptimized(True)
        Cv2.SetNumThreads(Math.Max(1, Environment.ProcessorCount))

        Dim trainDir = IO.Path.Combine(groupRoot, "training")
        Dim sampleDir = IO.Path.Combine(trainDir, "samples")
        IO.Directory.CreateDirectory(sampleDir)

        Dim fileName = $"tpl_{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.png"
        Dim filePath = IO.Path.Combine(sampleDir, fileName)

        Dim safePoly = polygon.
            Select(Function(p) New Point(
                Math.Max(0, Math.Min(source.Width - 1, p.X)),
                Math.Max(0, Math.Min(source.Height - 1, p.Y)))).
            ToArray()

        Dim minX = safePoly.Min(Function(p) p.X)
        Dim maxX = safePoly.Max(Function(p) p.X)
        Dim minY = safePoly.Min(Function(p) p.Y)
        Dim maxY = safePoly.Max(Function(p) p.Y)

        Dim safeRoi As New Rect(minX, minY, maxX - minX + 1, maxY - minY + 1)
        If safeRoi.Width <= 0 OrElse safeRoi.Height <= 0 Then Throw New ArgumentException("Polygon bbox invalid")

        ' 直接裁剪 ROI 區域儲存，不縮放到母版大小
        ' 多邊形遮罩讓 ROI 外的像素填為深色背景
        Using mask As Mat = Mat.Zeros(source.Size(), MatType.CV_8UC1),
              solidFill As New Mat(source.Size(), source.Type(), New Scalar(32, 32, 32))
            Cv2.FillPoly(mask, {safePoly}, Scalar.White)
            source.CopyTo(solidFill, mask)
            Using crop As New Mat(solidFill, safeRoi)
                Cv2.ImWrite(filePath, crop, {New ImageEncodingParam(ImwriteFlags.PngCompression, 3)})
            End Using
        End Using

        Dim now = DateTimeOffset.Now.ToUnixTimeMilliseconds()

        Dim indexFile = LoadIndex(groupRoot)
        indexFile.Samples.Add(New TrainingSampleMeta With {
            .FileName = fileName,
            .CreatedAt = now,
            .LastMatchedAt = now,
            .RoiX = safeRoi.X,
            .RoiY = safeRoi.Y,
            .RoiW = safeRoi.Width,
            .RoiH = safeRoi.Height,
            .MasterThreshold = params.MasterThreshold,
            .PyramidLevel = params.PyramidLevel,
            .MatchMethod = params.MatchMethod,
            .MinArea = params.MinArea,
            .CannyLow = params.CannyLow,
            .CannyHigh = params.CannyHigh,
            .AngleMin = params.AngleMin,
            .AngleMax = params.AngleMax,
            .AngleStep = params.AngleStep,
            .PolygonPoints = safePoly.Select(Function(p) New TrainingPoint With {.X = p.X, .Y = p.Y}).ToList()
        })

        PurgeOverflow(groupRoot, indexFile, Math.Max(1, params.MaxSamples))
        SaveIndex(groupRoot, indexFile)
        SaveParams(groupRoot, params)

        Return indexFile.Samples.Count
    End Function

    Private Shared Function ResolveTemplateSize(groupRoot As String, fallback As Size) As Size
        Try
            Dim candidatePaths As New List(Of String)()
            For Each cam In IO.Directory.GetDirectories(groupRoot, "cam*")
                candidatePaths.Add(IO.Path.Combine(cam, "template.png"))
            Next
            candidatePaths.Add(IO.Path.Combine(groupRoot, "template.png"))

            For Each p In candidatePaths
                If Not IO.File.Exists(p) Then Continue For
                Using mat = Cv2.ImRead(p, ImreadModes.Unchanged)
                    If mat IsNot Nothing AndAlso Not mat.Empty() Then
                        Return mat.Size()
                    End If
                End Using
            Next
        Catch ex As Exception
            Logger.Warn("[TemplateTraining] resolve template size failed: " & ex.Message)
        End Try

        Return fallback
    End Function

    Public Shared Sub TouchLatestMatched(groupPath As String)
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return

        Dim idx = LoadIndex(groupRoot)
        If idx.Samples.Count = 0 Then Return

        Dim latest = idx.Samples.OrderByDescending(Function(x) x.LastMatchedAt).ThenByDescending(Function(x) x.CreatedAt).First()
        latest.LastMatchedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        SaveIndex(groupRoot, idx)
    End Sub

    Public Shared Function GetTrainingSamples(groupPath As String) As List(Of TrainingSampleMeta)
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) Then Return New List(Of TrainingSampleMeta)()

        SyncLock _cacheLock
            If _sampleMetaCache.ContainsKey(groupRoot) Then
                Return _sampleMetaCache(groupRoot).
                    Select(Function(x) CloneMeta(x)).
                    OrderByDescending(Function(x) x.LastMatchedAt).
                    ThenByDescending(Function(x) x.CreatedAt).
                    ToList()
            End If
        End SyncLock

        Dim idx = LoadIndex(groupRoot)
        Dim samples = idx.Samples.OrderByDescending(Function(x) x.LastMatchedAt).ThenByDescending(Function(x) x.CreatedAt).ToList()

        SyncLock _cacheLock
            _sampleMetaCache(groupRoot) = samples.Select(Function(x) CloneMeta(x)).ToList()
        End SyncLock

        Return samples
    End Function

    Public Shared Function GetTrainingSampleMeta(groupPath As String, fileName As String) As TrainingSampleMeta
        If String.IsNullOrWhiteSpace(fileName) Then Return Nothing

        Dim samples = GetTrainingSamples(groupPath)
        Dim meta = samples.FirstOrDefault(Function(x) String.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        If meta Is Nothing Then Return Nothing
        Return CloneMeta(meta)
    End Function

    Public Shared Function LoadTrainingSampleImage(groupPath As String, fileName As String) As Mat
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) OrElse String.IsNullOrWhiteSpace(fileName) Then Return Nothing

        Dim cacheKey = IO.Path.Combine(groupRoot, "training", "samples", fileName)

        SyncLock _cacheLock
            If _sampleBytesCache.ContainsKey(cacheKey) Then
                Return Cv2.ImDecode(_sampleBytesCache(cacheKey), ImreadModes.Color)
            End If
        End SyncLock

        If Not IO.File.Exists(cacheKey) Then Return Nothing

        Dim bytes = IO.File.ReadAllBytes(cacheKey)
        SyncLock _cacheLock
            _sampleBytesCache(cacheKey) = bytes
        End SyncLock
        Return Cv2.ImDecode(bytes, ImreadModes.Color)
    End Function

    Public Shared Sub WarmupAll()
        Dim root = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates")
        If Not IO.Directory.Exists(root) Then Return

        SyncLock _cacheLock
            _sampleMetaCache.Clear()
            _sampleBytesCache.Clear()
        End SyncLock

        For Each groupRoot In IO.Directory.GetDirectories(root)
            Dim normalized = NormalizeGroupPath(groupRoot)
            If String.IsNullOrWhiteSpace(normalized) Then Continue For

            Dim idx = LoadIndex(normalized)
            Dim samples = idx.Samples.Select(Function(x) CloneMeta(x)).ToList()

            SyncLock _cacheLock
                _sampleMetaCache(normalized) = samples
            End SyncLock

            Dim sampleDir = IO.Path.Combine(normalized, "training", "samples")
            If Not IO.Directory.Exists(sampleDir) Then Continue For

            For Each sample In samples
                Dim filePath = IO.Path.Combine(sampleDir, sample.FileName)
                If Not IO.File.Exists(filePath) Then Continue For
                Try
                    Dim bytes = IO.File.ReadAllBytes(filePath)
                    SyncLock _cacheLock
                        _sampleBytesCache(filePath) = bytes
                    End SyncLock
                Catch ex As Exception
                    Logger.Warn("[TemplateTraining] warmup sample failed: " & ex.Message)
                End Try
            Next
        Next
    End Sub

    Public Shared Function DeleteTrainingSample(groupPath As String, fileName As String) As Boolean
        Dim groupRoot = NormalizeGroupPath(groupPath)
        If String.IsNullOrWhiteSpace(groupRoot) OrElse String.IsNullOrWhiteSpace(fileName) Then Return False

        Dim idx = LoadIndex(groupRoot)
        Dim item = idx.Samples.FirstOrDefault(Function(x) String.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase))
        If item Is Nothing Then Return False

        Dim sampleDir = IO.Path.Combine(groupRoot, "training", "samples")
        Dim filePath = IO.Path.Combine(sampleDir, item.FileName)
        If IO.File.Exists(filePath) Then
            IO.File.Delete(filePath)
        End If

        idx.Samples.Remove(item)
        SaveIndex(groupRoot, idx)

        SyncLock _cacheLock
            Dim cacheKey = IO.Path.Combine(groupRoot, "training", "samples", item.FileName)
            _sampleBytesCache.Remove(cacheKey)
            _sampleMetaCache.Remove(groupRoot)
        End SyncLock

        Return True
    End Function

    Private Shared Sub PurgeOverflow(groupRoot As String,
                                     idx As TrainingIndexFile,
                                     maxSamples As Integer)
        If idx.Samples.Count <= maxSamples Then Return

        Dim keep = idx.Samples.
            OrderByDescending(Function(x) x.LastMatchedAt).
            ThenByDescending(Function(x) x.CreatedAt).
            Take(maxSamples).
            ToList()

        Dim keepSet = New HashSet(Of String)(keep.Select(Function(x) x.FileName), StringComparer.OrdinalIgnoreCase)

        Dim sampleDir = IO.Path.Combine(groupRoot, "training", "samples")
        For Each s In idx.Samples
            If keepSet.Contains(s.FileName) Then Continue For
            Dim fp = IO.Path.Combine(sampleDir, s.FileName)
            If IO.File.Exists(fp) Then
                Try
                    IO.File.Delete(fp)
                Catch ex As Exception
                    Logger.Warn("[TemplateTraining] delete sample failed: " & ex.Message)
                End Try
            End If
        Next

        idx.Samples = keep
    End Sub

    Private Shared Function LoadIndex(groupRoot As String) As TrainingIndexFile
        Dim idxPath = IO.Path.Combine(groupRoot, "training", "training_index.json")
        If Not IO.File.Exists(idxPath) Then Return New TrainingIndexFile()

        Try
            Dim json = IO.File.ReadAllText(idxPath)
            Dim data = JsonSerializer.Deserialize(Of TrainingIndexFile)(json)
            If data Is Nothing Then Return New TrainingIndexFile()
            If data.Samples Is Nothing Then data.Samples = New List(Of TrainingSampleMeta)()
            Return data
        Catch ex As Exception
            Logger.Warn("[TemplateTraining] load index failed: " & ex.Message)
            Return New TrainingIndexFile()
        End Try
    End Function

    Private Shared Sub SaveIndex(groupRoot As String, idx As TrainingIndexFile)
        Dim dir = IO.Path.Combine(groupRoot, "training")
        IO.Directory.CreateDirectory(dir)
        Dim idxPath = IO.Path.Combine(dir, "training_index.json")
        Dim json = JsonSerializer.Serialize(idx, New JsonSerializerOptions With {.WriteIndented = True})
        IO.File.WriteAllText(idxPath, json)

        SyncLock _cacheLock
            _sampleMetaCache(groupRoot) = idx.Samples.Select(Function(x) CloneMeta(x)).ToList()
        End SyncLock
    End Sub

    Private Shared Sub SaveParams(groupRoot As String, params As TrainingTemplateParams)
        Dim dir = IO.Path.Combine(groupRoot, "training")
        IO.Directory.CreateDirectory(dir)
        Dim cfgPath = IO.Path.Combine(dir, "training_params.json")
        Dim json = JsonSerializer.Serialize(params, New JsonSerializerOptions With {.WriteIndented = True})
        IO.File.WriteAllText(cfgPath, json)
    End Sub

    Private Shared Function CloneMeta(source As TrainingSampleMeta) As TrainingSampleMeta
        If source Is Nothing Then Return Nothing

        Return New TrainingSampleMeta With {
            .FileName = source.FileName,
            .CreatedAt = source.CreatedAt,
            .LastMatchedAt = source.LastMatchedAt,
            .RoiX = source.RoiX,
            .RoiY = source.RoiY,
            .RoiW = source.RoiW,
            .RoiH = source.RoiH,
            .MasterThreshold = source.MasterThreshold,
            .PyramidLevel = source.PyramidLevel,
            .MatchMethod = source.MatchMethod,
            .MinArea = source.MinArea,
            .CannyLow = source.CannyLow,
            .CannyHigh = source.CannyHigh,
            .AngleMin = source.AngleMin,
            .AngleMax = source.AngleMax,
            .AngleStep = source.AngleStep,
            .PolygonPoints = source.PolygonPoints.Select(Function(p) New TrainingPoint With {.X = p.X, .Y = p.Y}).ToList()
        }
    End Function

End Class
