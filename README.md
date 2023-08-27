
Before starting configure number of nodes in the cluster in appsettings.json with the help of 'ClusterInfo.NodesCount' option.
# Results
Next, we are going to do some tests with three nodes cluster (```ClusterInfo.NodesCount=3```).
## Start two nodes
To start cluster with initially only two nodes running, run the following command: ```docker compose up --scale raftnode=2```. It's going to start two nodes:
![two-nodes-docker-resources.png](/results/two-nodes-docker-resources.png)
Then let's ensure that a single node is selected as Leader:
![two-nodes-leader.png](/results/two-nodes-leader.png)
![two-nodes-follower.png](/results/two-nodes-follower.png)
## Add two messages 
Now let's add ```msg1, msg2``` with the help of sending POST requests: ```/raft/command?command=msg1```, ```/raft/command?command=msg2``` to the Leader. Let's ensure that two messages where successfully replicated and commited (commitLength=2):
![two-nodes-follower-two-messages.png](/results/two-nodes-follower-two-messages.png)
![two-nodes-leader-two-messages.png](/results/two-nodes-leader-two-messages.png)
## Start thrid node
To start the third node tun the following command: ```docker compose up --scale raftnode=3```. Let's see whether thrid node is started and two previously added messages were replicated and commited:
![three-nodes-docker-resources.png](/results/three-nodes-docker-resources.png)
![three-nodes-third-follower-two-messages.png](/results/three-nodes-third-follower-two-messages.png)
## Partition a leader
To partition current leader let's delete it from network with the following command: ```docker network disconnect raft-aspnetcore_default raft-aspnetcore-raftnode-2```. Now, we can observe that there are two leaders in the cluster:
![three-nodes-partitioned-leader.png](/results/three-nodes-partitioned-leader.png)
![three-nodes-new-leader.png](/results/three-nodes-new-leader.png)
As you probably noticed we cannot longer talk to the partitioned leader through the same port, it's because we loose port forwarding when we disonnect container from network. In order to still be able to talk to the partitioned leader let's create the proxy (```alpine/socat``` docker image) which would forward all messages to the partitioned leader. To create a proxy run following command: ```docker run -d -p 8080:80 --name raftnode-2-proxy   -- alpine/socat TCP-LISTEN:80,fork TCP:172.17.0.3:443```.
## Add two messages through new leader
Now let's add ```msg3, msg4``` to the new leader. We can observe that two messages are replicated and committed on both nodes:
![three-nodes-leader-new-two-messages.png](/results/three-nodes-leader-new-two-messages.png)
![three-nodes-follower-new-two-messages.png](/results/three-nodes-follower-new-two-messages.png)
## Add new message to partitioned leader
Now let's add ```msg5``` to the partioned leader. We can observe that the ```msg5``` is added to the partitioned leader log:
![three-nodes-partitioned-leader-new-message.png](/results/three-nodes-partitioned-leader-new-message.png)
but because ```msg5``` cannot be replicated to the majority it cannot be commited, thus client is stuck waiting for response:
![three-nodes-client-stuck.png](/results/three-nodes-client-stuck.png)
## Join partitioned leader back to cluster
Let's connect partitioned leader and its proxy back to cluster with the following commands: ```docker network connect raft-aspnetcore_default raft-aspnetcore-raftnode-2```, ```docker network connect raft-aspnetcore_default raftnode-2-proxy```. Now we can see the ```msg5``` is overriden by ```msg3, msg4``` messages:
![three-nodes-joined-partitioned-leader.png](/results/three-nodes-joined-partitioned-leader.png)
