namespace tutar_glb.Models
{
    internal class SingleModel
    {
        public string name;
        public string id;
        public string model_signed_url;
        public File thumbnail;
        public File file;
        public List<Texture> textures;
        public string updatedAt;

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

        public class Texture
        {
            public File file;
        }
    }

    internal class SingleModelResponse
    {
        public SingleModel data;
    }
}
