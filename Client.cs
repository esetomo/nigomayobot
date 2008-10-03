using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Timers;
using libsecondlife;

namespace NigoMayo
{
    public class Client : SecondLife
    {
        public static readonly LLUUID OWNER = new LLUUID("1c9a4e68-70df-491a-b618-2427ce03ffbf");
        public uint m_owner_local_id;
        private readonly SixamoCS.Core m_sixamo;
        private Timer m_timer;

        public Client()
        {
            if (!System.IO.Directory.Exists("data"))
                System.IO.Directory.CreateDirectory("data");
            m_sixamo = SixamoCS.Create("data");

            m_timer = new Timer(10 * 1000);
            m_timer.Elapsed += new ElapsedEventHandler(m_timer_Elapsed);

            // Friends.OnFriendFound += new FriendsManager.FriendFoundEvent(Friends_OnFriendFound);
            Groups.OnGroupTitles += new GroupManager.GroupTitlesCallback(Groups_OnGroupTitles);
            Network.OnLogin += new NetworkManager.LoginCallback(Network_OnLogin);
            Objects.OnNewAvatar += new ObjectManager.NewAvatarCallback(Objects_OnNewAvatar);
            Objects.OnAvatarSitChanged += new ObjectManager.AvatarSitChanged(Objects_OnAvatarSitChanged);
            Objects.OnObjectUpdated += new ObjectManager.ObjectUpdatedCallback(Objects_OnObjectUpdated);
            Self.OnChat += new AgentManager.ChatCallback(Self_OnChat);
            Self.OnInstantMessage += new AgentManager.InstantMessageCallback(Self_OnInstantMessage);
            Self.OnTeleport += new AgentManager.TeleportCallback(Self_OnTeleport);
        }

        void Friends_OnFriendFound(LLUUID agentID, ulong regionHandle, LLVector3 location)
        {
            if (agentID == OWNER)
                Self.Teleport(regionHandle, location);
        }

        void m_timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if(SixamoCS.Rand() < 0.1)
                Talk();

            if (Network.CurrentSim == null)
                return;

            Avatar avatar;
            if (! Network.CurrentSim.ObjectsAvatars.TryGetValue(m_owner_local_id, out avatar) || avatar.ID != OWNER)
                Friends.MapFriend(OWNER);
        }

        void Objects_OnObjectUpdated(Simulator simulator, ObjectUpdate update, ulong regionHandle, ushort timeDilation)
        {
            if (!update.Avatar)
                return;

            if (update.LocalID == m_owner_local_id)
            {
                AutoPilotLocal(update.Position.X, update.Position.Y, update.Position.Z);
            }
        }

        void Objects_OnAvatarSitChanged(Simulator simulator, Avatar avatar, uint sittingOn, uint oldSeat)
        {
            if (avatar.ID == OWNER)
            {
                MoveToAvatar(avatar);
            }
        }

        void Objects_OnNewAvatar(Simulator simulator, Avatar avatar, ulong regionHandle, ushort timeDilation)
        {
            if (avatar.ID == OWNER)
            {
                m_owner_local_id = avatar.LocalID;
                MoveToAvatar(avatar);
            }
        }

        public void Login(string firstName, string lastName, string password)
        {
            var param = Network.DefaultLoginParams(firstName, lastName, password, "", "");
            // param.Start = "home";
	    // param.URI = "http://localhost:9000/";
            Network.BeginLogin(param);
            // LogLoader();
        }

        void Groups_OnGroupTitles(Dictionary<LLUUID, GroupTitle> titles)
        {
            // タイトルが日本語の場合、設定しなおさないと文字化けする。
            foreach (var item in titles)
                if (item.Value.Selected)
                    Groups.ActivateTitle(Self.ActiveGroup, item.Key);
        }

        void Self_OnTeleport(string message, AgentManager.TeleportStatus status, AgentManager.TeleportFlags flags)
        {
            switch (status)
            {
                case AgentManager.TeleportStatus.Finished:
                    Appearance.SetPreviousAppearance(false);
                    Self.Movement.AlwaysRun = true;
                    break;
            }
        }

        void Self_OnInstantMessage(InstantMessage im, Simulator simulator)
        {
            switch (im.Dialog)
            {
                case InstantMessageDialog.MessageFromAgent:
                    if (im.FromAgentID != OWNER)
                    {
                        Self.InstantMessage(OWNER, string.Format("{0}:{1}", im.FromAgentName, im.Message));
                    }
                    break;
                case InstantMessageDialog.RequestTeleport:
                    Self.TeleportLureRespond(im.FromAgentID, true);
                    break;
            }
        }

        void Network_OnLogin(LoginStatus login, string message)
        {
            Console.WriteLine("Login:{0},{1}", login, message);
            switch (login)
            {
                case LoginStatus.Success:
                    Appearance.SetPreviousAppearance(false);
                    Groups.RequestGroupTitles(Self.ActiveGroup);
                    Self.Movement.AlwaysRun = true;
                    m_timer.Enabled = true;
                    break;
            }
        }

        void Self_OnChat(string message, ChatAudibleLevel audible, ChatType type, ChatSourceType sourceType, string fromName, LLUUID id, LLUUID ownerid, LLVector3 position)
        {
            switch (type)
            {
                case ChatType.Normal:
                case ChatType.Shout:
                    Console.WriteLine("{0}>{1}", fromName, message);
                    if (sourceType == ChatSourceType.Agent)
                    {
                        if (id == Self.AgentID)
                            break;
                        m_sixamo.Memorize(message);

                        if (SixamoCS.Rand() > 1.0 / Math.Pow(Network.CurrentSim.ObjectsAvatars.Count - 1, 1.5))
                            break;

                        Talk();
                    }
                    break;
            }
        }

        void Talk()
        {
            string text = m_sixamo.Talk();
            int wait = text.Length * 200;
            if (wait > 3000)
                wait = 3000;

            Self.AnimationStart(Animations.TYPE, true);
            Self.Chat("", 0, ChatType.StartTyping);
            Timer timer = new Timer(wait);
            timer.Elapsed += (_, __) =>
            {
                Self.AnimationStop(Animations.TYPE, true);
                Self.AnimationStart(Animations.TALK, true);
                Self.Chat(text, 0, ChatType.Normal);
                timer.Enabled = false;
                timer.Dispose();
            };
            timer.Enabled = true;
        }

        void MoveToAvatar(Avatar avatar)
        {
            if (avatar.SittingOn > 0)
            {
                Primitive prim;
                if (Network.CurrentSim.ObjectsPrimitives.TryGetValue(avatar.SittingOn, out prim))
                    Self.RequestSit(prim.ID, LLVector3.Zero);
            }
            else
            {
                Self.Stand();
                AutoPilotLocal(avatar.Position.X, avatar.Position.Y, avatar.Position.Z);
                Self.Movement.TurnToward(avatar.Position);
            }
        }

        public void AutoPilotLocal(double localX, double localY, double z)
        {            
            uint x, y;
            Helpers.LongToUInts(Network.CurrentSim.Handle, out x, out y);
            Self.AutoPilot(x + localX, y + localY, z);
        }
    }
}
