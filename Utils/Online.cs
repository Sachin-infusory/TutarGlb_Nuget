using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media.Media3D;
using tutar_glb;
using tutar_glb.Models;
using tutar_glb.Utils;

namespace tutar.Utils
{
    internal class Online
    {
        internal static async Task<List<Model>> FetchModels(string model_name = "", Tag tag = null, int page = 0, int size = 10)
        {
            StringBuilder urlBuilder = new StringBuilder();
            urlBuilder.Append($@"models?page={page}&size={size}");
            if (!model_name.Equals(""))
            {
                urlBuilder.Append($@"&model_name={model_name}");
            }
            if (tag != null)
            {
                urlBuilder.Append($@"&tags={tag.id}");
            }

            string message;
            try
            {
                HttpResponseMessage response = await TutarGlb.client.GetAsync(urlBuilder.ToString());
                message = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    dynamic errorResponse = JsonConvert.DeserializeObject(message);
                    throw new Exceptions.NetworkException((string)errorResponse?.error ?? "something went wrong");
                }
            }
            catch (Exception err)
            {
                throw new Exceptions.NetworkException(err.Message);
            }

            AllModelsResponse allModelsResponse = JsonConvert.DeserializeObject<AllModelsResponse>(message);
            TutarGlb.s3BucketUrl = allModelsResponse.meta.s3_bucket_url;
            return allModelsResponse.data;
        }

        internal static async Task<Model3DGroup> FetchModel(string id)
        {
            SingleModelResponse singleModelResponse;
            string message;
            try
            {
                HttpResponseMessage response = await TutarGlb.client.GetAsync($@"models/{id}");
                message = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    dynamic errorResponse = JsonConvert.DeserializeObject(message);
                    throw new Exceptions.NetworkException((string)errorResponse?.error ?? "something went wrong");
                }
            }
            catch (Exception ex)
            {
                throw new Exceptions.NetworkException(ex.Message);
            }
            singleModelResponse = JsonConvert.DeserializeObject<SingleModelResponse>(message);
            return TutarGlb.LoadModal(singleModelResponse);
        }

        internal static async Task<List<Tag>> FetchTags(Tag parentTag = null)
        {
            StringBuilder urlBuilder = new StringBuilder();
            urlBuilder.Append("tags");
            if (parentTag == null)
            {
                urlBuilder.Append($@"?is_root=true");
            }
            else
            {
                urlBuilder.Append($@"?parent_tag_id={parentTag.id}");
            }

            string message;
            try
            {
                HttpResponseMessage response = await TutarGlb.client.GetAsync(urlBuilder.ToString());
                message = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    dynamic errorResponse = JsonConvert.DeserializeObject(message);
                    throw new Exceptions.NetworkException((string)errorResponse?.error ?? "something went wrong");
                }
            }
            catch (Exception ex)
            {
                throw new Exceptions.NetworkException(ex.Message);
            }

            Tags tags = JsonConvert.DeserializeObject<Tags>(message);
            return tags.data;
        }
    }
}
