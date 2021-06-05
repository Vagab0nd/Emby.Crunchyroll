using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Crunchyroll
{
    public class CrunchyrolChannel : IChannel, ISupportsLatestMedia
    {
        public string Name => "Crunchyroll";

        public string Description => string.Empty; //TODO: Add description

        public ChannelParentalRating ParentalRating => ChannelParentalRating.UsPG13;

        public InternalChannelFeatures GetChannelFeatures()
        {
            return new InternalChannelFeatures
            {
                ContentTypes = new List<ChannelMediaContentType>
                {
                    ChannelMediaContentType.Episode
                },

                MediaTypes = new List<ChannelMediaType>
                {
                    ChannelMediaType.Video
                },

                MaxPageSize = 100,

                DefaultSortFields = new List<ChannelItemSortField>
                {
                    ChannelItemSortField.Name,
                    ChannelItemSortField.PremiereDate,
                    ChannelItemSortField.Runtime,
                },

                SupportsContentDownloading = true,
                SupportsSortOrderToggle = true
            };
        }

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ImageType> GetSupportedChannelImages()
        {
            throw new NotImplementedException();
        }
    }
}
