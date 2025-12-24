using AVFoundation;
using CoreMedia;
using CoreVideo;
using CoreGraphics;
using Foundation;
using UIKit;
using CoreAnimation;

namespace CrownRFEP_Reader.Platforms.MacCatalyst;

/// <summary>
/// Implementación de composición de video para MacCatalyst usando AVFoundation
/// </summary>
public class MacVideoCompositionService : Services.IVideoCompositionService
{
    public bool IsAvailable => true;

    public async Task<Services.VideoExportResult> ExportParallelVideosAsync(
        Services.ParallelVideoExportParams parameters,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validar rutas antes de continuar
            if (string.IsNullOrEmpty(parameters.Video1Path) || !File.Exists(parameters.Video1Path))
            {
                return new Services.VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"El archivo de video 1 no existe: {parameters.Video1Path}"
                };
            }
            
            if (string.IsNullOrEmpty(parameters.Video2Path) || !File.Exists(parameters.Video2Path))
            {
                return new Services.VideoExportResult
                {
                    Success = false,
                    ErrorMessage = $"El archivo de video 2 no existe: {parameters.Video2Path}"
                };
            }

            System.Diagnostics.Debug.WriteLine($"[VideoComposition] Video1Path: {parameters.Video1Path}");
            System.Diagnostics.Debug.WriteLine($"[VideoComposition] Video2Path: {parameters.Video2Path}");

            // Ejecutar toda la lógica de AVFoundation en el hilo principal
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Cargar los assets de video
                var url1 = NSUrl.FromFilename(parameters.Video1Path);
                var url2 = NSUrl.FromFilename(parameters.Video2Path);
                
                var asset1 = AVAsset.FromUrl(url1);
                var asset2 = AVAsset.FromUrl(url2);

                if (asset1 == null || asset2 == null)
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = "Error creando assets de video"
                    };
                }

                // Esperar a que los assets estén listos
                var loadResult1 = await LoadAssetAsync(asset1);
                if (!loadResult1.success)
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = $"Error cargando video 1: {loadResult1.error}"
                    };
                }
                
                var loadResult2 = await LoadAssetAsync(asset2);
                if (!loadResult2.success)
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = $"Error cargando video 2: {loadResult2.error}"
                    };
                }

                // Obtener tracks
                var allTracks1 = asset1.Tracks;
                var allTracks2 = asset2.Tracks;
                
                System.Diagnostics.Debug.WriteLine($"[VideoComposition] Asset1 tiene {allTracks1?.Length ?? 0} tracks");
                System.Diagnostics.Debug.WriteLine($"[VideoComposition] Asset2 tiene {allTracks2?.Length ?? 0} tracks");

                // Buscar tracks de video
                var videoTrack1 = allTracks1?.FirstOrDefault(t => t.MediaType == AVMediaTypes.Video.GetConstant());
                var videoTrack2 = allTracks2?.FirstOrDefault(t => t.MediaType == AVMediaTypes.Video.GetConstant());

                if (videoTrack1 == null || videoTrack2 == null)
                {
                    // Log de debug
                    foreach (var track in allTracks1 ?? Array.Empty<AVAssetTrack>())
                    {
                        System.Diagnostics.Debug.WriteLine($"[VideoComposition] Track1 MediaType: '{track.MediaType}'");
                    }
                    
                    // Intentar búsqueda alternativa
                    videoTrack1 ??= allTracks1?.FirstOrDefault(t => 
                        t.MediaType?.ToString()?.Contains("vide") == true || 
                        t.MediaType?.ToString() == "public.movie");
                    videoTrack2 ??= allTracks2?.FirstOrDefault(t => 
                        t.MediaType?.ToString()?.Contains("vide") == true || 
                        t.MediaType?.ToString() == "public.movie");
                }

                if (videoTrack1 == null || videoTrack2 == null)
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = $"No se encontraron pistas de video. Asset1 tracks: {allTracks1?.Length ?? 0}, Asset2 tracks: {allTracks2?.Length ?? 0}"
                    };
                }

                // Tracks de audio (opcionales)
                var audioTrack1 = allTracks1?.FirstOrDefault(t => t.MediaType == AVMediaTypes.Audio.GetConstant());
                var audioTrack2 = allTracks2?.FirstOrDefault(t => t.MediaType == AVMediaTypes.Audio.GetConstant());

                // Calcular tamaños y duraciones
                var size1 = ApplyTransformToSize(videoTrack1.NaturalSize, videoTrack1.PreferredTransform);
                var size2 = ApplyTransformToSize(videoTrack2.NaturalSize, videoTrack2.PreferredTransform);

                var duration1 = asset1.Duration - CMTime.FromSeconds(parameters.Video1StartPosition.TotalSeconds, 600);
                var duration2 = asset2.Duration - CMTime.FromSeconds(parameters.Video2StartPosition.TotalSeconds, 600);
                
                // Usar la duración más corta
                var exportDuration = CMTime.Compare(duration1, duration2) < 0 ? duration1 : duration2;
                
                if (parameters.MaxDuration.HasValue)
                {
                    var maxDur = CMTime.FromSeconds(parameters.MaxDuration.Value.TotalSeconds, 600);
                    if (CMTime.Compare(maxDur, exportDuration) < 0)
                        exportDuration = maxDur;
                }

                // Calcular layout
                CGSize outputSize;
                CGRect frame1, frame2;

                if (parameters.IsHorizontalLayout)
                {
                    var targetHeight = Math.Min(size1.Height, size2.Height);
                    var scale1 = targetHeight / size1.Height;
                    var scale2 = targetHeight / size2.Height;
                    var width1 = size1.Width * scale1;
                    var width2 = size2.Width * scale2;
                    
                    outputSize = new CGSize(width1 + width2, targetHeight);
                    frame1 = new CGRect(0, 0, width1, targetHeight);
                    frame2 = new CGRect(width1, 0, width2, targetHeight);
                }
                else
                {
                    var targetWidth = Math.Min(size1.Width, size2.Width);
                    var scale1 = targetWidth / size1.Width;
                    var scale2 = targetWidth / size2.Width;
                    var height1 = size1.Height * scale1;
                    var height2 = size2.Height * scale2;
                    
                    outputSize = new CGSize(targetWidth, height1 + height2);
                    frame1 = new CGRect(0, height2, targetWidth, height1);
                    frame2 = new CGRect(0, 0, targetWidth, height2);
                }

                // Limitar a 1080p
                var maxDimension = 1920.0;
                if (outputSize.Width > maxDimension || outputSize.Height > maxDimension)
                {
                    var scaleFactor = maxDimension / Math.Max(outputSize.Width, outputSize.Height);
                    outputSize = new CGSize(outputSize.Width * scaleFactor, outputSize.Height * scaleFactor);
                    frame1 = new CGRect(frame1.X * scaleFactor, frame1.Y * scaleFactor, 
                                        frame1.Width * scaleFactor, frame1.Height * scaleFactor);
                    frame2 = new CGRect(frame2.X * scaleFactor, frame2.Y * scaleFactor, 
                                        frame2.Width * scaleFactor, frame2.Height * scaleFactor);
                }

                // Crear composición
                var composition = new AVMutableComposition();
                var compositionVideoTrack1 = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);
                var compositionVideoTrack2 = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);

                var startTime1 = CMTime.FromSeconds(parameters.Video1StartPosition.TotalSeconds, 600);
                var startTime2 = CMTime.FromSeconds(parameters.Video2StartPosition.TotalSeconds, 600);
                var timeRange1 = new CMTimeRange { Start = startTime1, Duration = exportDuration };
                var timeRange2 = new CMTimeRange { Start = startTime2, Duration = exportDuration };

                NSError? error;
                compositionVideoTrack1?.InsertTimeRange(timeRange1, videoTrack1, CMTime.Zero, out error);
                compositionVideoTrack2?.InsertTimeRange(timeRange2, videoTrack2, CMTime.Zero, out error);

                // Audio
                if (audioTrack1 != null)
                {
                    var audioCompositionTrack1 = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
                    audioCompositionTrack1?.InsertTimeRange(timeRange1, audioTrack1, CMTime.Zero, out error);
                }
                if (audioTrack2 != null)
                {
                    var audioCompositionTrack2 = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
                    audioCompositionTrack2?.InsertTimeRange(timeRange2, audioTrack2, CMTime.Zero, out error);
                }

                // Video composition instructions
                var instruction = new AVMutableVideoCompositionInstruction
                {
                    TimeRange = new CMTimeRange { Start = CMTime.Zero, Duration = exportDuration }
                };

                var layerInstruction1 = AVMutableVideoCompositionLayerInstruction.FromAssetTrack(compositionVideoTrack1!);
                var layerInstruction2 = AVMutableVideoCompositionLayerInstruction.FromAssetTrack(compositionVideoTrack2!);

                var transform1 = CalculateTransform(size1, frame1, videoTrack1.PreferredTransform);
                var transform2 = CalculateTransform(size2, frame2, videoTrack2.PreferredTransform);

                layerInstruction1.SetTransform(transform1, CMTime.Zero);
                layerInstruction2.SetTransform(transform2, CMTime.Zero);

                instruction.LayerInstructions = new[] { layerInstruction1, layerInstruction2 };

                var videoComposition = new AVMutableVideoComposition
                {
                    Instructions = new[] { instruction },
                    FrameDuration = new CMTime(1, 30),
                    RenderSize = outputSize
                };

                // Overlays
                AddTextOverlays(videoComposition, parameters, frame1, frame2, outputSize);

                // Exportar
                if (File.Exists(parameters.OutputPath))
                    File.Delete(parameters.OutputPath);

                var outputUrl = NSUrl.FromFilename(parameters.OutputPath);
                var exporter = new AVAssetExportSession(composition, AVAssetExportSessionPreset.HighestQuality);
                exporter.OutputUrl = outputUrl;
                exporter.OutputFileType = AVFileTypes.Mpeg4.GetConstant()!;
                exporter.VideoComposition = videoComposition;
                exporter.ShouldOptimizeForNetworkUse = true;

                var tcs = new TaskCompletionSource<bool>();

                // Monitor progress
                if (progress != null)
                {
                    _ = Task.Run(async () =>
                    {
                        while (exporter.Status == AVAssetExportSessionStatus.Exporting)
                        {
                            progress.Report(exporter.Progress);
                            await Task.Delay(100);
                        }
                    });
                }

                exporter.ExportAsynchronously(() =>
                {
                    tcs.TrySetResult(exporter.Status == AVAssetExportSessionStatus.Completed);
                });

                using (cancellationToken.Register(() => exporter.CancelExport()))
                {
                    await tcs.Task;
                }

                if (exporter.Status == AVAssetExportSessionStatus.Completed)
                {
                    var fileInfo = new FileInfo(parameters.OutputPath);
                    return new Services.VideoExportResult
                    {
                        Success = true,
                        OutputPath = parameters.OutputPath,
                        FileSizeBytes = fileInfo.Length,
                        Duration = TimeSpan.FromSeconds(exportDuration.Seconds)
                    };
                }
                else
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = exporter.Error?.LocalizedDescription ?? $"Error de exportación: {exporter.Status}"
                    };
                }
            });
        }
        catch (Exception ex)
        {
            return new Services.VideoExportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<(bool success, string? error)> LoadAssetAsync(AVAsset asset)
    {
        var tcs = new TaskCompletionSource<bool>();
        string? errorMessage = null;
        
        asset.LoadValuesAsynchronously(new[] { "tracks", "duration", "playable" }, () =>
        {
            var tracksStatus = asset.StatusOfValue(new NSString("tracks"), out var tracksError);
            var durationStatus = asset.StatusOfValue(new NSString("duration"), out var durationError);
            
            if (tracksStatus == AVKeyValueStatus.Failed)
            {
                errorMessage = tracksError?.LocalizedDescription ?? "Error cargando tracks";
            }
            else if (durationStatus == AVKeyValueStatus.Failed)
            {
                errorMessage = durationError?.LocalizedDescription ?? "Error cargando duración";
            }
            
            tcs.TrySetResult(tracksStatus == AVKeyValueStatus.Loaded && durationStatus == AVKeyValueStatus.Loaded);
        });
        
        var success = await tcs.Task;
        return (success, errorMessage);
    }

    private CGSize ApplyTransformToSize(CGSize size, CGAffineTransform transform)
    {
        var a = transform.A;
        var d = transform.D;
        
        if (Math.Abs(a) < 0.1 && Math.Abs(d) < 0.1)
        {
            return new CGSize(size.Height, size.Width);
        }
        return size;
    }

    private CGAffineTransform CalculateTransform(CGSize originalSize, CGRect targetFrame, CGAffineTransform videoTransform)
    {
        var scaleX = targetFrame.Width / originalSize.Width;
        var scaleY = targetFrame.Height / originalSize.Height;
        var scale = Math.Min(scaleX, scaleY);

        var a = videoTransform.A;
        var b = videoTransform.B;
        CGAffineTransform transform;
        
        if (Math.Abs(a) < 0.1 && Math.Abs(b - 1) < 0.1)
        {
            transform = CGAffineTransform.MakeRotation((nfloat)(Math.PI / 2));
            transform = CGAffineTransform.Multiply(transform, CGAffineTransform.MakeTranslation(originalSize.Height, 0));
        }
        else if (Math.Abs(a) < 0.1 && Math.Abs(b + 1) < 0.1)
        {
            transform = CGAffineTransform.MakeRotation((nfloat)(-Math.PI / 2));
            transform = CGAffineTransform.Multiply(transform, CGAffineTransform.MakeTranslation(0, originalSize.Width));
        }
        else
        {
            transform = CGAffineTransform.MakeIdentity();
        }

        transform = CGAffineTransform.Multiply(transform, CGAffineTransform.MakeScale((nfloat)scale, (nfloat)scale));
        transform = CGAffineTransform.Multiply(transform, CGAffineTransform.MakeTranslation(targetFrame.X, targetFrame.Y));

        return transform;
    }

    private void AddTextOverlays(AVMutableVideoComposition videoComposition, 
        Services.ParallelVideoExportParams parameters,
        CGRect frame1, CGRect frame2, CGSize outputSize)
    {
        var parentLayer = new CALayer();
        var videoLayer = new CALayer();
        
        parentLayer.Frame = new CGRect(0, 0, outputSize.Width, outputSize.Height);
        videoLayer.Frame = new CGRect(0, 0, outputSize.Width, outputSize.Height);
        parentLayer.AddSublayer(videoLayer);

        if (!string.IsNullOrEmpty(parameters.Video1AthleteName))
        {
            AddOverlayToFrame(parentLayer, frame1, 
                parameters.Video1AthleteName,
                parameters.Video1Category,
                parameters.Video1Section,
                parameters.Video1Time,
                parameters.Video1Penalties);
        }

        if (!string.IsNullOrEmpty(parameters.Video2AthleteName))
        {
            AddOverlayToFrame(parentLayer, frame2,
                parameters.Video2AthleteName,
                parameters.Video2Category,
                parameters.Video2Section,
                parameters.Video2Time,
                parameters.Video2Penalties);
        }

        videoComposition.AnimationTool = AVVideoCompositionCoreAnimationTool.FromLayer(videoLayer, parentLayer);
    }

    private void AddOverlayToFrame(CALayer parentLayer, CGRect frame, 
        string? athleteName, string? category, int? section, string? time, string? penalties)
    {
        // Construir el texto de información
        var infoText = "";
        if (!string.IsNullOrEmpty(category)) infoText += category;
        if (section.HasValue) infoText += (infoText.Length > 0 ? " | " : "") + $"Sec. {section}";
        if (!string.IsNullOrEmpty(time)) infoText += (infoText.Length > 0 ? " | " : "") + time;
        if (!string.IsNullOrEmpty(penalties)) infoText += (infoText.Length > 0 ? " | " : "") + penalties;

        // Calcular el ancho necesario basándose en el texto más largo
        var nameFontSize = 16.0f;
        var infoFontSize = 12.0f;
        var horizontalPadding = 24.0; // 12px padding a cada lado
        
        // Estimar el ancho del texto (aproximadamente 0.6 * fontSize * caracteres para fuente proporcional)
        var nameWidth = !string.IsNullOrEmpty(athleteName) 
            ? athleteName.Length * nameFontSize * 0.55 
            : 0;
        var infoWidth = !string.IsNullOrEmpty(infoText) 
            ? infoText.Length * infoFontSize * 0.55 
            : 0;
        
        var maxTextWidth = Math.Max(nameWidth, infoWidth);
        var containerWidth = Math.Min(maxTextWidth + horizontalPadding, frame.Width - 20); // No exceder el frame
        containerWidth = Math.Max(containerWidth, 120); // Mínimo 120px
        
        var overlayContainer = new CALayer();
        overlayContainer.Frame = new CGRect(frame.X + 10, frame.Y + frame.Height - 80, containerWidth, 70);
        overlayContainer.BackgroundColor = UIColor.FromRGBA(0, 0, 0, 150).CGColor;
        overlayContainer.CornerRadius = 8;

        var alignmentLeft = new NSString("left");

        if (!string.IsNullOrEmpty(athleteName))
        {
            var nameLayer = new CATextLayer();
            nameLayer.SetValueForKey(new NSString(athleteName), new NSString("string"));
            nameLayer.FontSize = nameFontSize;
            nameLayer.ForegroundColor = UIColor.White.CGColor;
            nameLayer.Frame = new CGRect(12, 40, containerWidth - 24, 24);
            nameLayer.SetValueForKey(alignmentLeft, new NSString("alignmentMode"));
            nameLayer.ContentsScale = UIScreen.MainScreen.Scale;
            overlayContainer.AddSublayer(nameLayer);
        }

        if (!string.IsNullOrEmpty(infoText))
        {
            var infoLayer = new CATextLayer();
            infoLayer.SetValueForKey(new NSString(infoText), new NSString("string"));
            infoLayer.FontSize = infoFontSize;
            infoLayer.ForegroundColor = UIColor.FromRGBA(180, 180, 180, 255).CGColor;
            infoLayer.Frame = new CGRect(12, 15, containerWidth - 24, 20);
            infoLayer.SetValueForKey(alignmentLeft, new NSString("alignmentMode"));
            infoLayer.ContentsScale = UIScreen.MainScreen.Scale;
            overlayContainer.AddSublayer(infoLayer);
        }

        parentLayer.AddSublayer(overlayContainer);
    }

    public async Task<Services.VideoExportResult> ExportQuadVideosAsync(
        Services.QuadVideoExportParams parameters,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validar rutas antes de continuar
            var videoPaths = new[] { parameters.Video1Path, parameters.Video2Path, parameters.Video3Path, parameters.Video4Path };
            for (int i = 0; i < videoPaths.Length; i++)
            {
                if (string.IsNullOrEmpty(videoPaths[i]) || !File.Exists(videoPaths[i]))
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = $"El archivo de video {i + 1} no existe: {videoPaths[i]}"
                    };
                }
            }

            System.Diagnostics.Debug.WriteLine($"[VideoComposition] Quad export - Video1Path: {parameters.Video1Path}");
            System.Diagnostics.Debug.WriteLine($"[VideoComposition] Quad export - Video2Path: {parameters.Video2Path}");
            System.Diagnostics.Debug.WriteLine($"[VideoComposition] Quad export - Video3Path: {parameters.Video3Path}");
            System.Diagnostics.Debug.WriteLine($"[VideoComposition] Quad export - Video4Path: {parameters.Video4Path}");

            // Ejecutar toda la lógica de AVFoundation en el hilo principal
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Cargar los assets de video
                var url1 = NSUrl.FromFilename(parameters.Video1Path);
                var url2 = NSUrl.FromFilename(parameters.Video2Path);
                var url3 = NSUrl.FromFilename(parameters.Video3Path);
                var url4 = NSUrl.FromFilename(parameters.Video4Path);

                var asset1 = AVAsset.FromUrl(url1);
                var asset2 = AVAsset.FromUrl(url2);
                var asset3 = AVAsset.FromUrl(url3);
                var asset4 = AVAsset.FromUrl(url4);

                if (asset1 == null || asset2 == null || asset3 == null || asset4 == null)
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = "Error creando assets de video"
                    };
                }

                // Esperar a que los assets estén listos
                var assets = new[] { asset1, asset2, asset3, asset4 };
                for (int i = 0; i < assets.Length; i++)
                {
                    var loadResult = await LoadAssetAsync(assets[i]);
                    if (!loadResult.success)
                    {
                        return new Services.VideoExportResult
                        {
                            Success = false,
                            ErrorMessage = $"Error cargando video {i + 1}: {loadResult.error}"
                        };
                    }
                }

                // Obtener tracks de video
                var videoTrack1 = GetVideoTrack(asset1);
                var videoTrack2 = GetVideoTrack(asset2);
                var videoTrack3 = GetVideoTrack(asset3);
                var videoTrack4 = GetVideoTrack(asset4);

                if (videoTrack1 == null || videoTrack2 == null || videoTrack3 == null || videoTrack4 == null)
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = "No se encontraron pistas de video en uno o más archivos"
                    };
                }

                // Tracks de audio (opcionales)
                var audioTrack1 = GetAudioTrack(asset1);
                var audioTrack2 = GetAudioTrack(asset2);
                var audioTrack3 = GetAudioTrack(asset3);
                var audioTrack4 = GetAudioTrack(asset4);

                // Calcular tamaños
                var size1 = ApplyTransformToSize(videoTrack1.NaturalSize, videoTrack1.PreferredTransform);
                var size2 = ApplyTransformToSize(videoTrack2.NaturalSize, videoTrack2.PreferredTransform);
                var size3 = ApplyTransformToSize(videoTrack3.NaturalSize, videoTrack3.PreferredTransform);
                var size4 = ApplyTransformToSize(videoTrack4.NaturalSize, videoTrack4.PreferredTransform);

                // Calcular duraciones desde las posiciones de inicio
                var startTimes = new[]
                {
                    CMTime.FromSeconds(parameters.Video1StartPosition.TotalSeconds, 600),
                    CMTime.FromSeconds(parameters.Video2StartPosition.TotalSeconds, 600),
                    CMTime.FromSeconds(parameters.Video3StartPosition.TotalSeconds, 600),
                    CMTime.FromSeconds(parameters.Video4StartPosition.TotalSeconds, 600)
                };

                var duration1 = asset1.Duration - startTimes[0];
                var duration2 = asset2.Duration - startTimes[1];
                var duration3 = asset3.Duration - startTimes[2];
                var duration4 = asset4.Duration - startTimes[3];

                // Usar la duración más corta
                var exportDuration = duration1;
                if (CMTime.Compare(duration2, exportDuration) < 0) exportDuration = duration2;
                if (CMTime.Compare(duration3, exportDuration) < 0) exportDuration = duration3;
                if (CMTime.Compare(duration4, exportDuration) < 0) exportDuration = duration4;

                if (parameters.MaxDuration.HasValue)
                {
                    var maxDur = CMTime.FromSeconds(parameters.MaxDuration.Value.TotalSeconds, 600);
                    if (CMTime.Compare(maxDur, exportDuration) < 0)
                        exportDuration = maxDur;
                }

                // Calcular layout 2x2
                // Usar el tamaño del video más pequeño como referencia para cada celda
                var cellWidth = Math.Min(Math.Min(size1.Width, size2.Width), Math.Min(size3.Width, size4.Width));
                var cellHeight = Math.Min(Math.Min(size1.Height, size2.Height), Math.Min(size3.Height, size4.Height));

                // Escalar cada video para que quepa en su celda manteniendo aspecto
                var scale1 = Math.Min(cellWidth / size1.Width, cellHeight / size1.Height);
                var scale2 = Math.Min(cellWidth / size2.Width, cellHeight / size2.Height);
                var scale3 = Math.Min(cellWidth / size3.Width, cellHeight / size3.Height);
                var scale4 = Math.Min(cellWidth / size4.Width, cellHeight / size4.Height);

                // Tamaño de salida: 2x2 celdas
                var outputSize = new CGSize(cellWidth * 2, cellHeight * 2);

                // Frames para cada video (cuadrícula 2x2)
                // Layout:  [1] [2]
                //          [3] [4]
                var frame1 = new CGRect(0, cellHeight, cellWidth, cellHeight);          // Arriba izquierda
                var frame2 = new CGRect(cellWidth, cellHeight, cellWidth, cellHeight);  // Arriba derecha
                var frame3 = new CGRect(0, 0, cellWidth, cellHeight);                   // Abajo izquierda
                var frame4 = new CGRect(cellWidth, 0, cellWidth, cellHeight);           // Abajo derecha

                // Limitar a 1080p
                var maxDimension = 1920.0;
                if (outputSize.Width > maxDimension || outputSize.Height > maxDimension)
                {
                    var scaleFactor = maxDimension / Math.Max(outputSize.Width, outputSize.Height);
                    outputSize = new CGSize(outputSize.Width * scaleFactor, outputSize.Height * scaleFactor);
                    frame1 = ScaleRect(frame1, scaleFactor);
                    frame2 = ScaleRect(frame2, scaleFactor);
                    frame3 = ScaleRect(frame3, scaleFactor);
                    frame4 = ScaleRect(frame4, scaleFactor);
                    cellWidth *= scaleFactor;
                    cellHeight *= scaleFactor;
                }

                // Crear composición
                var composition = new AVMutableComposition();
                var compositionVideoTrack1 = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);
                var compositionVideoTrack2 = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);
                var compositionVideoTrack3 = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);
                var compositionVideoTrack4 = composition.AddMutableTrack(AVMediaTypes.Video.GetConstant()!, 0);

                var timeRange1 = new CMTimeRange { Start = startTimes[0], Duration = exportDuration };
                var timeRange2 = new CMTimeRange { Start = startTimes[1], Duration = exportDuration };
                var timeRange3 = new CMTimeRange { Start = startTimes[2], Duration = exportDuration };
                var timeRange4 = new CMTimeRange { Start = startTimes[3], Duration = exportDuration };

                NSError? error;
                compositionVideoTrack1?.InsertTimeRange(timeRange1, videoTrack1, CMTime.Zero, out error);
                compositionVideoTrack2?.InsertTimeRange(timeRange2, videoTrack2, CMTime.Zero, out error);
                compositionVideoTrack3?.InsertTimeRange(timeRange3, videoTrack3, CMTime.Zero, out error);
                compositionVideoTrack4?.InsertTimeRange(timeRange4, videoTrack4, CMTime.Zero, out error);

                // Audio (mezclamos los 4)
                if (audioTrack1 != null)
                {
                    var audioCompositionTrack1 = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
                    audioCompositionTrack1?.InsertTimeRange(timeRange1, audioTrack1, CMTime.Zero, out error);
                }
                if (audioTrack2 != null)
                {
                    var audioCompositionTrack2 = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
                    audioCompositionTrack2?.InsertTimeRange(timeRange2, audioTrack2, CMTime.Zero, out error);
                }
                if (audioTrack3 != null)
                {
                    var audioCompositionTrack3 = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
                    audioCompositionTrack3?.InsertTimeRange(timeRange3, audioTrack3, CMTime.Zero, out error);
                }
                if (audioTrack4 != null)
                {
                    var audioCompositionTrack4 = composition.AddMutableTrack(AVMediaTypes.Audio.GetConstant()!, 0);
                    audioCompositionTrack4?.InsertTimeRange(timeRange4, audioTrack4, CMTime.Zero, out error);
                }

                // Video composition instructions
                var instruction = new AVMutableVideoCompositionInstruction
                {
                    TimeRange = new CMTimeRange { Start = CMTime.Zero, Duration = exportDuration }
                };

                var layerInstruction1 = AVMutableVideoCompositionLayerInstruction.FromAssetTrack(compositionVideoTrack1!);
                var layerInstruction2 = AVMutableVideoCompositionLayerInstruction.FromAssetTrack(compositionVideoTrack2!);
                var layerInstruction3 = AVMutableVideoCompositionLayerInstruction.FromAssetTrack(compositionVideoTrack3!);
                var layerInstruction4 = AVMutableVideoCompositionLayerInstruction.FromAssetTrack(compositionVideoTrack4!);

                var transform1 = CalculateTransform(size1, frame1, videoTrack1.PreferredTransform);
                var transform2 = CalculateTransform(size2, frame2, videoTrack2.PreferredTransform);
                var transform3 = CalculateTransform(size3, frame3, videoTrack3.PreferredTransform);
                var transform4 = CalculateTransform(size4, frame4, videoTrack4.PreferredTransform);

                layerInstruction1.SetTransform(transform1, CMTime.Zero);
                layerInstruction2.SetTransform(transform2, CMTime.Zero);
                layerInstruction3.SetTransform(transform3, CMTime.Zero);
                layerInstruction4.SetTransform(transform4, CMTime.Zero);

                instruction.LayerInstructions = new[] { layerInstruction1, layerInstruction2, layerInstruction3, layerInstruction4 };

                var videoComposition = new AVMutableVideoComposition
                {
                    Instructions = new[] { instruction },
                    FrameDuration = new CMTime(1, 30),
                    RenderSize = outputSize
                };

                // Overlays
                AddQuadTextOverlays(videoComposition, parameters, frame1, frame2, frame3, frame4, outputSize);

                // Exportar
                if (File.Exists(parameters.OutputPath))
                    File.Delete(parameters.OutputPath);

                var outputUrl = NSUrl.FromFilename(parameters.OutputPath);
                var exporter = new AVAssetExportSession(composition, AVAssetExportSessionPreset.HighestQuality);
                exporter.OutputUrl = outputUrl;
                exporter.OutputFileType = AVFileTypes.Mpeg4.GetConstant()!;
                exporter.VideoComposition = videoComposition;
                exporter.ShouldOptimizeForNetworkUse = true;

                var tcs = new TaskCompletionSource<bool>();

                // Monitor progress
                if (progress != null)
                {
                    _ = Task.Run(async () =>
                    {
                        while (exporter.Status == AVAssetExportSessionStatus.Exporting)
                        {
                            progress.Report(exporter.Progress);
                            await Task.Delay(100);
                        }
                    });
                }

                exporter.ExportAsynchronously(() =>
                {
                    tcs.TrySetResult(exporter.Status == AVAssetExportSessionStatus.Completed);
                });

                using (cancellationToken.Register(() => exporter.CancelExport()))
                {
                    await tcs.Task;
                }

                if (exporter.Status == AVAssetExportSessionStatus.Completed)
                {
                    var fileInfo = new FileInfo(parameters.OutputPath);
                    return new Services.VideoExportResult
                    {
                        Success = true,
                        OutputPath = parameters.OutputPath,
                        FileSizeBytes = fileInfo.Length,
                        Duration = TimeSpan.FromSeconds(exportDuration.Seconds)
                    };
                }
                else
                {
                    return new Services.VideoExportResult
                    {
                        Success = false,
                        ErrorMessage = exporter.Error?.LocalizedDescription ?? $"Error de exportación: {exporter.Status}"
                    };
                }
            });
        }
        catch (Exception ex)
        {
            return new Services.VideoExportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private AVAssetTrack? GetVideoTrack(AVAsset asset)
    {
        var allTracks = asset.Tracks;
        var videoTrack = allTracks?.FirstOrDefault(t => t.MediaType == AVMediaTypes.Video.GetConstant());
        
        if (videoTrack == null)
        {
            // Intentar búsqueda alternativa
            videoTrack = allTracks?.FirstOrDefault(t =>
                t.MediaType?.ToString()?.Contains("vide") == true ||
                t.MediaType?.ToString() == "public.movie");
        }
        
        return videoTrack;
    }

    private AVAssetTrack? GetAudioTrack(AVAsset asset)
    {
        return asset.Tracks?.FirstOrDefault(t => t.MediaType == AVMediaTypes.Audio.GetConstant());
    }

    private CGRect ScaleRect(CGRect rect, double factor)
    {
        return new CGRect(rect.X * factor, rect.Y * factor, rect.Width * factor, rect.Height * factor);
    }

    private void AddQuadTextOverlays(AVMutableVideoComposition videoComposition,
        Services.QuadVideoExportParams parameters,
        CGRect frame1, CGRect frame2, CGRect frame3, CGRect frame4, CGSize outputSize)
    {
        var parentLayer = new CALayer();
        var videoLayer = new CALayer();

        parentLayer.Frame = new CGRect(0, 0, outputSize.Width, outputSize.Height);
        videoLayer.Frame = new CGRect(0, 0, outputSize.Width, outputSize.Height);
        parentLayer.AddSublayer(videoLayer);

        if (!string.IsNullOrEmpty(parameters.Video1AthleteName))
        {
            AddOverlayToFrame(parentLayer, frame1,
                parameters.Video1AthleteName,
                parameters.Video1Category,
                parameters.Video1Section,
                parameters.Video1Time,
                parameters.Video1Penalties);
        }

        if (!string.IsNullOrEmpty(parameters.Video2AthleteName))
        {
            AddOverlayToFrame(parentLayer, frame2,
                parameters.Video2AthleteName,
                parameters.Video2Category,
                parameters.Video2Section,
                parameters.Video2Time,
                parameters.Video2Penalties);
        }

        if (!string.IsNullOrEmpty(parameters.Video3AthleteName))
        {
            AddOverlayToFrame(parentLayer, frame3,
                parameters.Video3AthleteName,
                parameters.Video3Category,
                parameters.Video3Section,
                parameters.Video3Time,
                parameters.Video3Penalties);
        }

        if (!string.IsNullOrEmpty(parameters.Video4AthleteName))
        {
            AddOverlayToFrame(parentLayer, frame4,
                parameters.Video4AthleteName,
                parameters.Video4Category,
                parameters.Video4Section,
                parameters.Video4Time,
                parameters.Video4Penalties);
        }

        videoComposition.AnimationTool = AVVideoCompositionCoreAnimationTool.FromLayer(videoLayer, parentLayer);
    }
}
