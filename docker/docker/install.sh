
sudo cp docker-compose /usr/bin/docker-compose
sudo chmod +x /usr/bin/docker-compose


sudo tar -xvf docker-23.0.1.tgz
sudo cp docker/* /usr/bin/

sudo mkdir -p /etc/docker
sudo cp ./daemon.json /etc/docker/daemon.json

sudo cp docker.service /etc/systemd/system/docker.service
sudo chmod +x /etc/systemd/system/docker.service

sudo systemctl daemon-reload
sudo systemctl enable docker.service
sudo systemctl start docker
