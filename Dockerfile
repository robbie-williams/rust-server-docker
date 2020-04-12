FROM ubuntu:18.04

LABEL maintainer="Robbie"

# Setup default environment variables for the server
ENV RUST_SERVER_STARTUP_ARGUMENTS "-batchmode -load -nographics +server.secure 1"
ENV RUST_SERVER_IDENTITY "rustacean"
ENV RUST_SERVER_SALT "456456874687"
ENV RUST_SERVER_SEED "768"
ENV RUST_SERVER_NAME "RUSTACEAN"
ENV RUST_SERVER_DESCRIPTION ""
ENV RUST_SERVER_URL "https://rustacean.io"
ENV RUST_SERVER_BANNER_URL "https://rustacean.s3-ap-southeast-2.amazonaws.com/rustacean-header.png"
ENV RUST_RCON_WEB "1"
ENV RUST_RCON_PORT "28016"
ENV RUST_RCON_PASSWORD "rustytrombone!"
ENV RUST_UPDATE_CHECKING "1"
ENV RUST_UPDATE_BRANCH "public"
ENV RUST_START_MODE "0"
ENV RUST_OXIDE_ENABLED "1"
ENV RUST_OXIDE_UPDATE_ON_BOOT "1"
ENV RUST_SERVER_WORLDSIZE "1500"
ENV RUST_SERVER_MAXPLAYERS "25"
ENV RUST_SERVER_SAVE_INTERVAL "600"

# Fix apt-get warnings
ARG DEBIAN_FRONTEND=noninteractive

# Install dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        curl \
        nginx \
        expect \
        ca-certificates \
        npm \
        unzip \
        tcl \
        bzip2 \
        lib32gcc1 \
        locales \
        p7zip-full \
        tar \
        wget \
	bsdtar \
        libgdiplus && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Remove default nginx stuff
RUN rm -fr /usr/share/nginx/html/* && \
	rm -fr /etc/nginx/sites-available/* && \
	rm -fr /etc/nginx/sites-enabled/*

# Install webrcon (specific commit)
COPY nginx_rcon.conf /etc/nginx/nginx.conf
RUN curl --output /tmp/gh-pages.zip -sL https://github.com/Facepunch/webrcon/archive/gh-pages.zip && unzip /tmp/gh-pages.zip -d /tmp/gh-pages/ && \
        mv /tmp/gh-pages/* /usr/share/nginx/html/ && \
        rm -rf /tmp/gh-pages

# Customize the webrcon package to fit our needs
ADD fix_conn.sh /tmp/fix_conn.sh

##Install Steam
ARG PUID=1000

ENV STEAMCMDDIR /steamcmd

# Install, update & upgrade packages
# This also creates the home directory we later need
# Clean TMP, apt-get cache and other stuff to make the image smaller
# Create Directory for SteamCMD
# Download SteamCMD
# Extract and delete archive
RUN mkdir -p ${STEAMCMDDIR} \
	&& wget -qO- 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz' | tar zxf - -C ${STEAMCMDDIR} \
	&& apt-get remove --purge -y \
		wget \
	&& apt-get clean autoclean \
	&& apt-get autoremove -y \
	&& rm -rf /var/lib/apt/lists/*

WORKDIR $STEAMCMDDIR

VOLUME $STEAMCMDDIR

# Create the volume directories
RUN mkdir -p /${STEAMCMDDIR}/rust /usr/share/nginx/html /var/log/nginx

# Add plugins
COPY rust_config/oxide/ /${STEAMCMDDIR}/rust/oxide

#Add admins
COPY rust_config/users.cfg /${STEAMCMDDIR}/rust/server/${RUST_SERVER_IDENTITY}/cfg/

# Setup proper shutdown support
ADD shutdown_app/ /app/shutdown_app/
WORKDIR /app/shutdown_app
RUN npm install

# Setup restart support (for update automation)
ADD restart_app/ /app/restart_app/
WORKDIR /app/restart_app
RUN npm install

# Setup scheduling support
ADD scheduler_app/ /app/scheduler_app/
WORKDIR /app/scheduler_app
RUN npm install

# Setup rcon command relay app
ADD rcon_app/ /app/rcon_app/
WORKDIR /app/rcon_app
RUN npm install
RUN ln -s /app/rcon_app/app.js /usr/bin/rcon

# Add the steamcmd installation script
ADD install.txt /app/install.txt

# Copy the Rust startup script
ADD start_rust.sh /app/start.sh

# Copy the Rust update check script
ADD update_check.sh /app/update_check.sh

# Copy extra files
COPY README.md LICENSE.md /app/

# Set the current working directory
WORKDIR /

# Fix permissions
RUN chown -R 1000:1000 \
    /${STEAMCMDDIR} \
    /app \
    /usr/share/nginx/html \
    /var/log/nginx

# Run as a non-root user by default
ENV PGID 1000
ENV PUID 1000

# Expose necessary ports
EXPOSE 8080
EXPOSE 28015
EXPOSE 28016

# Define directories to take ownership of
ENV CHOWN_DIRS "/app,/steamcmd,/usr/share/nginx/html,/var/log/nginx"

# Expose the volumes
VOLUME [ "/steamcmd/rust" ]

# Start the server
CMD [ "bash", "/app/start.sh"]
