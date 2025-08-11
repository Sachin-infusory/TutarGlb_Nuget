using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using tutar_glb.Models;

namespace tutar_glb.Utils
{
    internal class ObjBundle
    {
        public static Model3DGroup FetchModel(string id)
        {
            if (!File.Exists("Models/syllabus.json"))
            {
                return null;
            }
            dynamic data;
            using (StreamReader r = new StreamReader("Models/syllabus.json"))
            {
                string json = r.ReadToEnd();
                data = JsonConvert.DeserializeObject(json);
            }
            List<SingleModel> modelList = data.models.ToObject<List<SingleModel>>();
            SingleModelResponse singleModelResponse;
            singleModelResponse = new SingleModelResponse();
            singleModelResponse.data = modelList.Find(_model => _model.id.Equals(id));
            return TutarGlb.LoadModal(singleModelResponse);
        }
    }
}
