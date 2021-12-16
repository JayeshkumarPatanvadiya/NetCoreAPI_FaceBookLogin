namespace NetCoreAPI_FaceBookLogin_BackEnd.Controllers
{
    public class Message
    {
        public string[] vs;
        public string email;
        public string callback;
        private string v;
        private object p;

        public Message(string[] vs, string email, string callback, object p)
        {
            this.vs = vs;
            this.v = email;
            this.callback = callback;
            this.p = p;
        }
    }
}