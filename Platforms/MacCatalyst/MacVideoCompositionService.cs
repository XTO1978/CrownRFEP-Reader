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
}
