using System;
using Windows.Data.Json;
using 云湖WP.Api.Common;

namespace 云湖WP.Api.User.Edit
{
    /// <summary>
    /// 用户资料修改请求参数
    /// </summary>
    public class UserEditRequest
    {
        public string Name { get; set; }
        public string Introduction { get; set; }
        public int Gender { get; set; } // 1-男, 2-女, 3-保密/其他
        public long Birthday { get; set; }
        public string Province { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public string LocationCode { get; set; }

        public UserEditRequest()
        {
            Introduction = "";
            Gender = 3;
            Province = "";
            City = "";
            District = "";
            LocationCode = "";
        }

        public string ToJson()
        {
            var reqObj = new JsonObject();
            if (!string.IsNullOrEmpty(Name))
            {
                reqObj.SetNamedValue("name", JsonValue.CreateStringValue(Name));
            }
            reqObj.SetNamedValue("introduction", JsonValue.CreateStringValue(Introduction ?? ""));
            reqObj.SetNamedValue("gender", JsonValue.CreateNumberValue(Gender));
            reqObj.SetNamedValue("birthday", JsonValue.CreateNumberValue(Birthday));
            reqObj.SetNamedValue("province", JsonValue.CreateStringValue(Province ?? ""));
            reqObj.SetNamedValue("city", JsonValue.CreateStringValue(City ?? ""));
            reqObj.SetNamedValue("district", JsonValue.CreateStringValue(District ?? ""));
            reqObj.SetNamedValue("locationCode", JsonValue.CreateStringValue(LocationCode ?? ""));
            return reqObj.Stringify();
        }
    }

    /// <summary>
    /// 用户资料修改结果模型
    /// </summary>
    public class UserEditResult : ApiResult
    {
        public UserEditResult()
        {
            Code = 1;
        }
    }
}
