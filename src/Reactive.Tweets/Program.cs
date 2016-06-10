using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Tweetinvi;
using Tweetinvi.Core.Authentication;
using Tweetinvi.Core.Interfaces;
using Tweetinvi.Core.Parameters;

namespace Reactive.Tweets
{
    static class Program
    {
        private const string ConsumerKey = "";
        private const string ConsumerSecret = "";
        private const string AccessToken = "";
        private const string AccessTokenSecret = "";

        private static readonly Location CenterOfNewYork = new Location(new Coordinates(-74, 40),
            new Coordinates(-73, 41));

        static void Main(string[] args)
        {
            var tweetSource = Source.ActorRef<ITweet>(100, OverflowStrategy.DropHead);
            var formatFlow = Flow.Create<ITweet>().Select(FormatTweet);
            var writeSink = Sink.ForEach<string>(Console.WriteLine);
            var countAutors = Flow.Create<ITweet>()
                .StatefulSelectMany(() =>
                {
                    var dict = new Dictionary<string, int>();

                    Func<ITweet, IEnumerable<string>> result = (tweet =>
                    {
                        var user = tweet.CreatedBy.Name;
                        if (!dict.ContainsKey(user))
                            dict.Add(user, 1);

                        return new[] { $"{dict[user]++} tweet from {user}\n" };
                    });

                    return result;
                });

            var graph = GraphDsl.Create(countAutors, writeSink, (notUsed, _) => notUsed, (b, count, write) =>
            {
                var broadcast = b.Add(new Broadcast<ITweet>(2));
                var output = b.From(broadcast.Out(0)).Via(formatFlow);
                b.From(broadcast.Out(1)).Via(count).To(write);
                return new FlowShape<ITweet, string>(broadcast.In, output.Out);
            });

            using (var sys = ActorSystem.Create("Reactive-Tweets"))
            {
                using (var mat = sys.Materializer())
                {
                    // Start Akka.Net stream
                    var actor = tweetSource.Via(graph).To(writeSink).Run(mat);

                    // Start Twitter stream
                    Auth.SetCredentials(new TwitterCredentials(ConsumerKey, ConsumerSecret, AccessToken,
                        AccessTokenSecret));
                    var stream = Stream.CreateFilteredStream();
                    stream.AddLocation(CenterOfNewYork);
                    stream.MatchingTweetReceived += (_, arg) => actor.Tell(arg.Tweet); // push the tweets into the stream
                    stream.StartStreamMatchingAllConditions();

                    Console.ReadLine();
                }
            }
        }

        private static string FormatTweet(ITweet tweet)
        {
            var builder = new StringBuilder();
            builder.AppendLine("---------------------------------------------------------");
            builder.AppendLine($"Tweet from NewYork from: {tweet.CreatedBy} :");
            builder.AppendLine();
            builder.AppendLine(tweet.Text);
            builder.AppendLine();
            builder.AppendLine($"Hashtags: {tweet.Hashtags.Aggregate("", (s, entity) => s + entity.Text + ", ")}");
            builder.AppendLine("---------------------------------------------------------");
            builder.AppendLine();

            return builder.ToString();
        }
    }
}
