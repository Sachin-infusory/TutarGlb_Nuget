using System.Collections.Generic;

namespace tutar_glb.Models
{
    internal class AllModelsResponse
    {
        public List<Model> data;
        public Meta meta;

        public class Meta
        {
            public string s3_bucket_url;
        }
    }

    public class Model
    {
        public string name { get; set; }
        public string id;
        public File file;
        public File thumbnail;
        public string updatedAt;
        public List<ModelTag> ModelTags;

        public class File
        {
            public string id;
            public string path;
            public Metadata metadata;

            public class Metadata
            {
                public string file_name;
                public string file_type;
            }
        }

        public class ModelTag
        {
            public Tag tag;
        }
    }
}
