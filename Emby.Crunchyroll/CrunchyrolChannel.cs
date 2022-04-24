using Crunchyroll.Api;
using Crunchyroll.Api.Models;
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
        private static readonly HashSet<string> TempFiles = new HashSet<string>();
        private readonly ICrunchyrollApi crunchyrollApi;
        private readonly ILogger logger;

        public CrunchyrolChannel(ILogManager logManager, IFfmpegManager ffmpegManager)
        {
            crunchyrollApi = new CrunchyrollApi("vagab0nd007+cr@outlook.com", "n62MapPMiiuv2r", "en-US");
            logger = logManager.GetLogger(GetType().Name);
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

        public Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
        {
            if (!int.TryParse(id, out var mediaId))
            {
                throw new InvalidOperationException("Failed getting internal channel media item: " + id);
            }

            if (!TryGetMedia(mediaId, out var media))
            {
                throw new InvalidOperationException("Failed getting media info from crunchyroll for media id: " + id);
            }

            List<MediaSourceInfo> mediaList = new List<MediaSourceInfo>();
            logger.Debug("Serving internal channel media item stream: " + media.StreamData.Streams.First().Url);
            MediaSourceInfo mediaSourceInfo = new MediaSourceInfo()
            {
                Id = id,
                Name = media.Name,
                Path = media.StreamData.Streams.First().Url,
                RunTimeTicks = TimeSpan.FromSeconds(media.Duration).Ticks,
                IsRemote = true,
                SupportsDirectPlay = true,
                Protocol = MediaProtocol.Http,
                SupportsDirectStream = true,
                MediaStreams = new List<MediaStream> {
                        new MediaStream
                        {
                            Type = MediaStreamType.Video,
                            Index =  0

                        },
                        new MediaStream
                        {
                            Type = MediaStreamType.Audio,
                            Index =  1
                        }
                }
            };
            mediaList.Add(mediaSourceInfo);

            return Task.FromResult(mediaList.AsEnumerable());
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
    }
}
