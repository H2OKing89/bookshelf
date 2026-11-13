FROM alpine:3.21 AS build

RUN apk add --no-cache bash build-base cmake curl xz
WORKDIR /src
COPY scripts/build-mediainfo.sh scripts/mediainfo.lock scripts/
COPY scripts/mediainfo-licenses/ scripts/mediainfo-licenses/
COPY docker/mediainfo.Dockerfile docker/mediainfo.Dockerfile
ARG MEDIAINFO_BUILD_JOBS=4
ENV MEDIAINFO_BUILD_JOBS=${MEDIAINFO_BUILD_JOBS}
RUN scripts/build-mediainfo.sh --output /bundle

FROM scratch AS bundle
COPY --from=build /bundle/ /
