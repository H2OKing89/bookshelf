FROM alpine:3.24.4 AS mediainfo

RUN apk add --no-cache bash build-base cmake curl xz
WORKDIR /src
COPY scripts/build-mediainfo.sh scripts/mediainfo.lock scripts/
COPY scripts/mediainfo-licenses/ scripts/mediainfo-licenses/
COPY docker/mediainfo.Dockerfile docker/mediainfo.Dockerfile
RUN scripts/build-mediainfo.sh --output /bundle

FROM ghcr.io/linuxserver/baseimage-alpine:3.24

ARG TARGETPLATFORM

ENV XDG_CONFIG_HOME="/config/xdg" \
    COMPlus_EnableDiagnostics=0 \
    TMPDIR=/run/readarr-temp

RUN apk add -U --upgrade --no-cache \
    icu-libs \
    sqlite-libs \
    xmlstarlet

ARG GIT_BRANCH
ARG COMMIT_HASH
ARG BUILD_DATE
ARG METADATA_URL=https://api.bookinfo.pro
ARG HARDCOVER

LABEL maintainer="pennydreadful"

ENV METADATA_URL=${METADATA_URL}
ENV HARDCOVER=${HARDCOVER}

RUN mkdir -p /app/readarr/bin

RUN --mount=type=bind,source=_output,target=_output \
    case "$TARGETPLATFORM" in \
    "linux/amd64") cp -a /_output/net10.0/linux-musl-x64/publish/. /app/readarr/bin/ ;; \
    "linux/arm64") cp -a /_output/net10.0/linux-musl-arm64/publish/. /app/readarr/bin/ ;; \
    *) echo "Unsupported Bookshelf container platform: $TARGETPLATFORM. Use linux/amd64 or linux/arm64." >&2 && exit 1 ;; \
    esac; \
    cp -r /_output/UI /app/readarr/bin/UI

COPY --from=mediainfo /bundle/ /app/readarr/bin/tools/mediainfo/
RUN /app/readarr/bin/tools/mediainfo/mediainfo --Version

RUN echo -e "UpdateMethod=docker\nBranch=${GIT_BRANCH}\nPackageVersion=${COMMIT_HASH}}\nPackageAuthor=pennydreadful" > /app/readarr/package_info && \
    printf "version: ${COMMIT_HASH}}\nBuild-date: ${BUILD_DATE}" > /build_version

# copy local files
COPY docker/root/ /

# ports and volumes
EXPOSE 8787

VOLUME /config
