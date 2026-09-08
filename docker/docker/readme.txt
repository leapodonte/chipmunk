查看配置是否生效
docker inspect --format='{{.HostConfig.LogConfig}}' <container_id>

查看docker-compose.yml目录
docker inspect <容器ID> | grep com.docker.compose