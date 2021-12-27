using Crunchyroll.Api;
using Crunchyroll.Api.Models;
using DotNetTools.SharpGrabber;
using DotNetTools.SharpGrabber.Converter;
using DotNetTools.SharpGrabber.Grabbed;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrunchyrollModels = Crunchyroll.Api.Models;

namespace Emby.Crunchyroll
{
    public class CrunchyrolChannel : IChannel, ISupportsLatestMedia, IRequiresMediaInfoCallback
    {
        private static readonly HttpClient Client = new HttpClient();
        private static readonly HashSet<string> TempFiles = new HashSet<string>();
        private readonly ICrunchyrollApi crunchyrollApi;
        private readonly ILogger logger;
        private readonly IFfmpegManager ffmpegManager;

        public CrunchyrolChannel(ILogManager logManager, IFfmpegManager ffmpegManager)
        {
            crunchyrollApi = new CrunchyrollApi("vagab0nd007+cr@outlook.com", "n62MapPMiiuv2r", "en-US");
            logger = logManager.GetLogger(GetType().Name);
            this.ffmpegManager = ffmpegManager;
        }

        public string Name => "Crunchyroll";

        public string Description => string.Empty; //TODO: Add description

        public ChannelParentalRating ParentalRating => ChannelParentalRating.UsPG13;

        public string[] Attributes => throw new NotImplementedException();

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Episode,
                },

                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Video
                },

                MaxPageSize = 100,

                DefaultSortFields = new List<ChannelItemSortField>
                {
                    ChannelItemSortField.Name,
                    ChannelItemSortField.DateCreated,
                    ChannelItemSortField.Runtime,
                },

                SupportsContentDownloading = true,
                SupportsSortOrderToggle = true
            };
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            var path = GetType().Namespace + ".Resources.logo.png";

            return Task.FromResult(new DynamicImageResponse
            {
                Format = ImageFormat.Png,
                Stream = GetType().Assembly.GetManifestResourceStream(path)
            });
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            logger.Debug("Getting internal channel items: " + query.FolderId);
            ChannelItemResult result;

            if (query.FolderId == null)
            {
                result = await GetSeries(query, cancellationToken).ConfigureAwait(false);
            }
            else if (query.FolderId.EndsWith("s"))
            {
                result = await GetSeasons(query, cancellationToken).ConfigureAwait(false);
            }
            else if (query.FolderId.EndsWith("c"))
            {
                result = await GetEpisodes(query, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException($"Unknown folder id {query.FolderId}");
            }

            return result;
        }

        private async Task<ChannelItemResult> GetSeries(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var queueItems = await crunchyrollApi.ListQueue(CrunchyrollModels.MediaType.Anime);

            var resultItems = queueItems.Select(qi => new ChannelItemInfo
            {
                Type = ChannelItemType.Folder,
                ImageUrl = qi.Series.PortraitImage.FullUrl,
                Name = qi.Series.Name,
                Id = $"{qi.Series.SeriesId}s",
                Overview = qi.Series.Description,
                HomePageUrl = qi.Series.Url,
                Genres = qi.Series.Genres?.ToList(),
                CommunityRating = qi.Series.Rating,
                FolderType = ChannelFolderType.Container,
                IndexNumber = qi.Series.SeriesId
            })
            .ToList();

            return new ChannelItemResult
            {
                Items = resultItems,
                TotalRecordCount = resultItems.Count
            };
        }

        private async Task<ChannelItemResult> GetSeasons(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            if (!int.TryParse(query.FolderId.TrimEnd('s'), out var seriesId))
            {
                logger.Debug("Failed getting internal channel items: " + query.FolderId);
            }

            var resultItems = new List<ChannelItemInfo>();
            var seasons = await crunchyrollApi.ListCollections(seriesId);

            foreach (var season in seasons.Where(s => !s.Name.Contains("Dub)")))
            {
                resultItems.Add(new ChannelItemInfo
                {
                    Type = ChannelItemType.Folder,
                    //ImageUrl = season.PortraitImage.FullUrl, //TODO: other source?
                    Name = season.Name,
                    Id = $"{season.CollectionId}c",
                    Overview = season.Description,
                    ParentIndexNumber = season.SeriesId,
                    IndexNumber = season.Season,
                    FolderType = ChannelFolderType.Container
                });
                logger.Debug($"Added crunchyroll season {season.Season} - {season.Name} of seriesId {season.SeriesId}");
            }

            return new ChannelItemResult
            {
                Items = resultItems,
                TotalRecordCount = resultItems.Count,
            };
        }

        private async Task<ChannelItemResult> GetEpisodes(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            if (!int.TryParse(query.FolderId.TrimEnd('c'), out var collectionId))
            {
                logger.Debug("Failed getting internal channel items: " + query.FolderId);
            }

            var resultItems = new List<ChannelItemInfo>();
            var episodes = await crunchyrollApi.ListMedia(collectionId, true);
            var collection = await crunchyrollApi.GetInfo<Collection>(collectionId);
            foreach (var episode in episodes)
            {
                var item = new ChannelItemInfo
                {
                    Type = ChannelItemType.Media,
                    ImageUrl = episode.ScreenshotImage.FullUrl,
                    Name = episode.Name,
                    Id = episode.MediaId.ToString(),
                    Overview = episode.Description,
                    SeriesName = episode.SeriesName,
                    ContentType = ChannelMediaContentType.Episode,
                    HomePageUrl = episode.Url,
                    MediaType = ChannelMediaType.Video,
                    ParentIndexNumber = collection.Season,
                    IndexNumber = int.TryParse(episode.EpisodeNumber, out var indexNumber) ? indexNumber : (int?)null,
                    OriginalTitle = episode.Name

                };
                resultItems.Add(item);
                logger.Debug($"Added crunchyroll episode {item.IndexNumber} - {item.Name} of collectionId {episode.CollectionId}, index {item.ParentIndexNumber}");
            }

            return new ChannelItemResult
            {
                Items = resultItems,
                TotalRecordCount = resultItems.Count,
            };
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            return new List<ImageType>
            {
                ImageType.Thumb,
                ImageType.Primary,
                ImageType.Backdrop
            };
        }

        public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
        {
            if (!int.TryParse(id, out var mediaId))
            {
                throw new InvalidOperationException("Failed getting internal channel media item: " + id);
            }

            if (!TryGetMedia(mediaId, out var media))
            {
                throw new InvalidOperationException("Failed getting media info from crunchyroll for media id: " + id);
            }

            var outputFolderName = $"{media.SeriesName}/{media.CollectionName}";
            var grabbedFile = await FetchFile(media.StreamData.Streams.First().Url, outputFolderName);
            List<MediaSourceInfo> mediaList = new List<MediaSourceInfo>();
            logger.Debug("Serving internal channel media item stream: " + grabbedFile);
            MediaSourceInfo mediaSourceInfo = new MediaSourceInfo()
            {
                Id = id,
                Name = media.Name,
                Path = grabbedFile,
                RunTimeTicks = TimeSpan.FromSeconds(media.Duration).Ticks,
                IsRemote = false,
                SupportsDirectPlay = true,
                Protocol = MediaProtocol.File,
                SupportsDirectStream = true
                //MediaStreams = new List<MediaStream> {
                //        new MediaStream
                //        {
                //            Type = MediaStreamType.Video,
                //            Index =  0

                //        },
                //        new MediaStream
                //        {
                //            Type = MediaStreamType.Audio,
                //            Index =  1
                //        }
                //}
            };
            mediaList.Add(mediaSourceInfo);

            return mediaList;
        }

        private async Task<string> FetchFile(string hlsFile, string outputFolder)
        {
            var ffmpegPath = this.ffmpegManager.FfmpegConfiguration.EncoderPath;
            logger.Debug($"Using ffmpeg from: {ffmpegPath}");
            FFmpeg.AutoGen.ffmpeg.RootPath = ffmpegPath;

            return await Grab(new Uri(hlsFile), outputFolder);
        }

        private bool TryGetMedia(int mediaId, out Media media)
        {
            media = null;
            try
            {
                media = crunchyrollApi.GetInfo<Media>(mediaId).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<string> Grab(Uri uri, string outputFolder)
        {
            var grabber = GrabberBuilder.New()
                .UseDefaultServices()
                .AddHls()
                .Build();

            logger.Debug($"Grabbing from {uri}...");
            var grabResult = await grabber.GrabAsync(uri).ConfigureAwait(false);

            var reference = grabResult.Resource<GrabbedHlsStreamReference>();
            if (reference != null)
            {
                // Redirect to an M3U8 playlist
                return await Grab(reference.ResourceUri, outputFolder);
            }

            var metadataResources = grabResult.Resources<GrabbedHlsStreamMetadata>().ToArray();
            if (metadataResources.Length > 0)
            {
                // Description for one or more M3U8 playlists
                GrabbedHlsStreamMetadata selection;
                if (metadataResources.Length == 1)
                {
                    selection = metadataResources.Single();
                }
                else
                {
                    logger.Debug("=== Streams ===");
                    for (var i = 0; i < metadataResources.Length; i++)
                    {
                        var res = metadataResources[i];
                        logger.Debug("{0}. {1}", i + 1, $"{res.Name} {res.Resolution}");
                    }
                    var bestStream = metadataResources.OrderByDescending(x => x.Resolution.Width).First();
                    logger.Debug("Selected a stream: ");
                    logger.Debug($"{bestStream.Name} {bestStream.Resolution}");
                    selection = bestStream;
                }

                // Get information from the HLS stream
                var grabbedStream = await selection.Stream.Value;
                return await Grab(grabbedStream, selection, grabResult, outputFolder);
            }

            throw new Exception("Could not grab the HLS stream.");
        }

        private async Task<string> Grab(GrabbedHlsStream stream, GrabbedHlsStreamMetadata metadata, GrabResult grabResult, string outputFolder)
        {
            logger.Debug("=== Downloading ===");
            logger.Debug("{0} segments", stream.Segments.Count);
            logger.Debug("Duration: {0}", stream.Length);

            var tempFiles = new List<string>();
            try
            {
                for (var i = 0; i < stream.Segments.Count; i++)
                {
                    var segment = stream.Segments[i];
                    logger.Debug($"Downloading segment #{i + 1} {segment.Title}...");
                    var outputPath = Path.GetTempFileName();
                    tempFiles.Add(outputPath);
                    using(var httpClient = new HttpClient())
                    using(var responseStream = await httpClient.GetStreamAsync(segment.Uri))
                    using(var inputStream = await grabResult.WrapStreamAsync(responseStream))
                    using(var outputStream = new FileStream(outputPath, FileMode.Create))
                    {
                        await inputStream.CopyToAsync(outputStream);
                    }
                }

                return CreateOutputFile(tempFiles, metadata, outputFolder);
            }
            finally
            {
                foreach (var tempFile in tempFiles)
                {
                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
                logger.Debug("Cleaned up temp files.");
            }
        }

        private string CreateOutputFile(List<string> tempFiles, GrabbedHlsStreamMetadata metadata, string outputFolder)
        {
            logger.Debug("All segments were downloaded successfully."); 
            var outputPath = "/share/CACHEDEV2_DATA/Video/Anime/" + outputFolder;
            var concatenator = new MediaConcatenator(outputPath)
            {
                OutputMimeType = metadata.OutputFormat.Mime,
                OutputExtension = metadata.OutputFormat.Extension,
            };
            foreach (var tempFile in tempFiles)
            {
                concatenator.AddSource(tempFile);
            }
            concatenator.Build();
            logger.Debug("Output file created successfully at!");

            return outputPath;
        }
    }
}
