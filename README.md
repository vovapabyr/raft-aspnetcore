
# Raft consensus algorithm
The purpose of this project is to build state machine replication with total order broadcast following guidelines of the [In Search of an Understandable Consensus Algorithm](https://raft.github.io/raft.pdf) paper using .net platform. The solution consists of two projects:
 - [RaftCore](/src/RaftCore/) - as it's in its name, it's the core project which actualy implements the Raft protocol. Here are some things worth to mention:
    - Grpc is used as the communication protocol between nodes. Check [raft.proto](/src/RaftCore/Protos/raft.proto) to see the defined messages and services.
    - [Akk.NET](https://getakka.net/), which implements actor model, that solves the problem of concurrency in clear and concise way, removing any kind of need in pesimistic locking.
    - According to the Raft specification, a node of the cluster in any given time could be in a single possible role: Follower, Candidate and Leader.
    ![raft-fsm.png](/results/raft-fsm.png)
     As we can see, there are a set of rules which define the transition from one role to another. Having this kind of node's roles notation represnted, it's very convenient to think of the node as a finite state machine which switches between the roles. Akka framework gives out of the box the class [FSM<TState, TData>](https://getakka.net/articles/actors/finite-state-machine.html) that represents an actor as the finite state machine. That's why the core of the Raft protocal - [RaftActor](/src/RaftCore/Actors/RaftActor.cs) is implemeted as the FSM.
    - The other benefit of implementing the Raft protocal as the FSM, is that each role has some specific properties related only to its state. For example, ```_votesReceived``` property is only applicable to node being a Candidate, that's why we can represent Candidate as a seperate state [CandidateNodeState](/src/RaftCore/States/CandidateNodeState.cs), which would mean that only the candidate can gather the votes. And once, the node is transitioned to another role, all that votes are automatically removed, as the state is different and doesn't have ```_votesReceived``` property anymore. So, the big advantage of this approach that we doesn't need to track all the places to keep ex. ```_votesReceived``` property in corerect state. The same and even more is about [LeaderNodeState](/src/RaftCore/States/LeaderNodeState.cs) - we don't need to worry to keep ```_nextIndex```, ```_matchIndex``` properties in a correct state when the node is transiotioned from Leader to other roles and back. See all the states in the [States](/src/RaftCore/States/) folder.
    - The actor system has the following structure:
    ![actor-system.png](/results/actor-system.png)
        - ```raft-root-actor``` - is a core actor represented as a [RaftActor](/src/RaftCore/Actors/RaftActor.cs) class, and is created in a single instance.
        - ```raft-message-broadcast-actor``` - is created in a single instance by ```raft-root-actor``` and is represented [MessageBroadcastActor](/src/RaftCore/Actors/MessageBroadcastActor.cs) class, which is responsible for communication.
        - ```raft-request-vote```, ```raft-request-reponse```, ```raft-append-entries-request```, ```raft-append-entries-response``` - actors that are the instances of [MessageDispatcherActor](/src/RaftCore/Actors/MessageDispatcherActor.cs) class. They are all repsponsible for getting the grpc client for the correct node and dispatch needed action. All of these actors are created and supervised by [MessageBroadcastActor](/src/RaftCore/Actors/MessageBroadcastActor.cs) and for each request a new [MessageDispatcherActor](/src/RaftCore/Actors/MessageDispatcherActor.cs) is created and is stopped right after it sends the request. Note, if the requests fails ex. because of node is being unavailable, the actor will fail and propogate error to the father [MessageBroadcastActor](/src/RaftCore/Actors/MessageBroadcastActor.cs), which would just stop the actor. In a default behaviour father restart the actor, that's why we need to set supervision strategy to ```SupervisorStrategy.StoppingStrategy```, check the ```Props()```of the [MessageBroadcastActor](/src/RaftCore/Actors/MessageBroadcastActor.cs).
    - One of the edge cases of the Raft protocol is to ensure that the leader doesn't commit log entry from the previous term:
    ![leader-cannot-commit-prev-term.png](/results/leader-cannot-commit-prev-term.png)
    To see how the implementation take that into account, check ```TryCommitLogEntries()``` method of the [LeaderNodeState.cs](/src/RaftCore/States/LeaderNodeState.cs).
    - According to the Raft specification only the Leader can receive requests to add new messages from the client, and thus only the Leader can send the acknowledgemnt that the message is commited back to the client. So, it's logical that the ```_pendingResponses``` property should be located in the [LeaderNodeState](/src/RaftCore/States/LeaderNodeState.cs), but the reason it's in the base [NodeState](/src/RaftCore/States/NodeState.cs) is because we want to preserve this information through nodes transition and still be able to respond to client when the node becomes a Leader again (ex. if the Leader is downgraded to Follower and then is selected a Leader again, we still want the Leader be able to respond to the client). But, all the manipulations methods of the ```_pendingResponses``` property are hidden in the [LeaderNodeState](/src/RaftCore/States/LeaderNodeState.cs).     
 - [RaftNode](/src/RaftNode/) - asp.net core project, that represents the node of the cluster running the Raft protocol. To deploy cluster of three nodes run the following command ```docker compose up --scale raftnode=3```. The nodes communication relies on docker container names, that consist of two parts:
    -  container's name prefix - usually it's the folder's name where the [docker-compose.yml](/docker-compose.yml) is located + the name of the service specified in the [docker-compose.yml](/docker-compose.yml), which is ```raftnode``` by default. So, for example if folder's name is ```asp-netcore``` and service's name remains ```raftnode```, then prefix would be ```asp-netcore-raftnode```.
    - container's index - by default docker compose add index when ```--scale``` parametr is more than one.
    So, if we run ```docker compose up --scale raftnode=3``` it would create three nodes with the names: ```asp-netcore-raftnode-1```, ```asp-netcore-raftnode-2```, ```asp-netcore-raftnode-3```. In order nodes can communicate we need to set in [appsettings.json](/src/RaftNode/appsettings.json) the ```NodeNamePrefix``` option to ```raft-aspnetcore-raftnode``` and ```NodesCount``` to 3 (the same value we set for --scale parameter). Note, this is not the part of the Raft specification and is an example of simple discovery process.    

## Results
Next, we are going to do some tests with three nodes cluster (```ClusterInfo.NodesCount=3```).
### Start two nodes
To start cluster with initially only two nodes running, run the following command: ```docker compose up --scale raftnode=2```. It's going to start two nodes:
![two-nodes-docker-resources.png](/results/two-nodes-docker-resources.png)
Then let's ensure that a single node is selected as Leader:
![two-nodes-leader.png](/results/two-nodes-leader.png)
![two-nodes-follower.png](/results/two-nodes-follower.png)
### Add two messages 
Now let's add ```msg1, msg2``` with the help of sending POST requests: ```/raft/command?command=msg1```, ```/raft/command?command=msg2``` to the Leader. Let's ensure that two messages were successfully replicated and committed (commitLength=2):
![two-nodes-follower-two-messages.png](/results/two-nodes-follower-two-messages.png)
![two-nodes-leader-two-messages.png](/results/two-nodes-leader-two-messages.png)
### Start the third node
To start the third node run the following command: ```docker compose up --scale raftnode=3```. Let's see whether the third node is started and two previously added messages were replicated and committed:
![three-nodes-docker-resources.png](/results/three-nodes-docker-resources.png)
![three-nodes-third-follower-two-messages.png](/results/three-nodes-third-follower-two-messages.png)
### Partition a leader
To partition the current leader let's delete it from the network with the following command: ```docker network disconnect raft-aspnetcore_default raft-aspnetcore-raftnode-2```. Now, we can observe that there are two leaders in the cluster (new leader with the higher term):
![three-nodes-partitioned-leader.png](/results/three-nodes-partitioned-leader.png)
![three-nodes-new-leader.png](/results/three-nodes-new-leader.png)
As you probably noticed we cannot longer talk to the partitioned leader through the same port, it's because we lose port forwarding when we disconnect a container from a network. In order to still be able to talk to the partitioned leader let's create the proxy (```alpine/socat``` docker image) which would forward all messages to the partitioned leader. To create a proxy run the following command: ```docker run -d -p 8080:80 --name raftnode-2-proxy -- alpine/socat TCP-LISTEN:80,fork TCP:172.17.0.3:443```.
### Add two messages through the new leader
Now let's add ```msg3, msg4``` to the new leader. We can observe that two messages are replicated and committed on both nodes:
![three-nodes-leader-new-two-messages.png](/results/three-nodes-leader-new-two-messages.png)
![three-nodes-follower-new-two-messages.png](/results/three-nodes-follower-new-two-messages.png)
### Add a new message to the partitioned leader
Now let's add ```msg5``` to the partitioned leader. We can observe that the ```msg5``` is added to the partitioned leader log:
![three-nodes-partitioned-leader-new-message.png](/results/three-nodes-partitioned-leader-new-message.png)

but because ```msg5``` cannot be replicated to the majority it cannot be committed, thus the client is stuck waiting for a response:
![three-nodes-client-stuck.png](/results/three-nodes-client-stuck.png)
### Join partitioned leader back to cluster
Let's connect the partitioned leader and its proxy back to the cluster with the following commands: ```docker network connect raft-aspnetcore_default raft-aspnetcore-raftnode-2```, ```docker network connect raft-aspnetcore_default raftnode-2-proxy```. Now we can see the ```msg5``` is overridden by ```msg3, msg4``` messages:
![three-nodes-joined-partitioned-leader.png](/results/three-nodes-joined-partitioned-leader.png)
