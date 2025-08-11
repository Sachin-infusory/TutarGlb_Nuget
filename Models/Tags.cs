using System.Collections.Generic;

namespace tutar_glb.Models
{
    internal class Tags
    {
        public List<Tag> data;
    }

    public class Tag
    {
        public string id;
        public string name;
        public string parent_tag_id;
        public bool is_root;
    }
}
