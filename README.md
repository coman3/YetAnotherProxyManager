# YetAnotherProxyManager

A modern, feature-rich reverse proxy manager built with ASP.NET Core and YARP, designed to replace traditional proxy managers with a more intuitive and integrated experience. Finally a proxy manager for the casual home server operator.

> **Note:** This project is in very early development. Features are actively being built and APIs may change significantly. Simply do not use for production yet.

---

## Overview

YetAnotherProxyManager (YAPM) aims to provide enterprise-grade reverse proxy capabilities through a clean, user-friendly web interface. Built on Microsoft's YARP (Yet Another Reverse Proxy), it combines powerful routing features with automated LetsEncrypt SSL management, real-time analytics, and sophisticated access control-all in a single, self-contained application.

---

## Features

### HTTP Reverse Proxy Routing
- **Host-based routing** - Route traffic by hostname to multiple upstream servers
- **Path-based routing** - Match and forward requests based on URL path prefixes
- **Load balancing** - Multiple strategies including round-robin, random, least requests, and power of two choices
- **Custom headers** - Inject or modify request and response headers per route
- **Configurable timeouts** - Granular control over connect, request, and response timeouts
- **Retry policies** - Automatic retries with configurable conditions

### TCP/UDP Stream Forwarding
- Native TCP and UDP port forwarding
- Real-time http connection tracking and statistics
- Dynamic configuration updates without restart
- Configurable buffer sizes and timeouts

### SSL/TLS Certificate Management
- **Let's Encrypt integration** - Automated ACME-based certificate provisioning
- **SNI support** - Dynamic certificate selection per hostname
- **Auto-renewal** - Background service monitors and renews certificates before expiry
- **Custom certificates** - Support for self-signed or externally managed certificates

### Advanced Access Control & Filtering
A sophisticated multi-layer filtering system with support for:

| Filter Type | Capabilities |
|-------------|--------------|
| **IP-based** | Single IP, IP ranges, CIDR notation, predefined rules (local-only, private-only) |
| **Geolocation** | Country and continent-level filtering with GeoIP lookups |
| **Time-based** | Day of week, time windows, date ranges with timezone support |
| **Header-based** | Custom HTTP header matching with regex support |

Filter rules can be combined into groups with AND/OR logic, priority ordering, and negation support.

### Service Registry
- Centralized service catalog with multiple endpoints per service
- Integration support for Portainer, TrueNAS, Docker, etc for easy setup of existing services, with one click to proxy guides
- Routes can reference services for automatic URL resolution
- Dependency tracking prevents accidental deletion of in-use services

### Real-Time Analytics
- Live request tracking with in-memory circular buffer (1M requests)
- Metrics including requests per minute, average response times, status code distribution, content type, etc.
- Geographic visualization with interactive world map
- Filtering by country, host, path, and status code
- Time-series charting with configurable grouping

### Management Interface
A modern web UI built with Blazor and MudBlazor featuring:
- Dashboard with system overview
- Route management with full CRUD operations
- Certificate status and renewal management
- Visual filter rule builder
- Service registry editor
- Real-time analytics dashboard with charts and maps
- Global settings configuration

---

## Architecture Highlights

- **Isolated management UI** - Admin interface served at a configurable `/.proxy-manager` base path, keeping management traffic separate from proxied requests but still easy to access 
- **Single executable** - Self-contained with embedded LiteDB database, easy to deploy and host
- **Multi-port architecture** - Configurable HTTP, HTTPS ports
- **Event-driven updates** - Real-time configuration changes without service interruption
- **Background services** - Automated certificate renewal and stream forwarding

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend | ASP.NET Core (.NET 10) |
| Reverse Proxy | YARP 2.x |
| Database | LiteDB |
| SSL/ACME | Certes |
| Frontend | Blazor Server |
| UI Components | MudBlazor |
| Charts | Blazor-ApexCharts |

---

## Roadmap

Planned features for future development:

### Authentication & Access
- **User Accounts & Route Authentication** - Configurable user accounts with route-based authentication, allowing you to protect services behind a single unified login
- **Social & SSO Integration** - Support for OAuth providers and single sign-on for seamless, streamlined access to protected routes

### Diagnostics & Monitoring
- **Route Preview & Testing** - Test internal routing to services as if requests were coming through externally, making it easy to diagnose network or connection issues without leaving the management UI
- **Automated Event Triggers** - Configurable actions triggered by events such as service downtime, failed authentication attempts, SSL certificate errors, and more

### Network Configuration
- **Router & DNS Setup Guides** - Built-in guides to help configure network traffic routing into your reverse proxy, covering common router setups and DNS configuration
- **Dynamic DNS Service** - Simple IP monitoring with automatic updates to Cloudflare and other DNS providers when your external IP changes
- **Network Tunnel Support** - Easy configuration for tunneling solutions (Cloudflare Tunnel, etc.) enabling proxy access without port forwarding

### Desktop Integration
- **Native Desktop Client** - Lightweight desktop application for quickly proxying local services, managing routes, and monitoring traffic without opening a browser

---

## Project Status

This project is in **active early development**. Current focus areas include:

- Core routing functionality
- UI / UX overhaul 
- Certificate management
- Analytics infrastructure, storage and filtering
- Configuration improvments

Contributions, feedback, and feature suggestions are welcome as the project matures.

---

## License

*License information to be added, currently no licence is defined and as such production deployments, forking, etc are not recommended at this time*
