using System.Runtime.Serialization; 

namespace TelegramBot
{
    [DataContract]
    public class Contact {

        [DataMember(Name="phone_number")]
        public string PhoneNumber	 { get; set; }
 
        [DataMember(Name="first_name")]
        public string FirstName { get; set; }

         
        [DataMember(Name="last_name")]
        public string LastName { get; set; } 
         

        [DataMember(Name="user_id")]
        public int UserID { get; set; }
        
    }   
}
