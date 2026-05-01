# Zonar Hub – Product Requirements Document (PRD)

## 1. Overview

Zonar Hub is a platform designed to manage sports tournaments, player registrations, scheduling, and match organization, with potential automation via messaging platforms (e.g., WhatsApp).

The system aims to simplify tournament organization while allowing flexible configuration depending on tournament type.

---

## 2. Objectives

* Simplify tournament creation and management
* Automate player registration and scheduling
* Provide intelligent grouping (zones / fixtures)
* Allow integration with messaging systems (WhatsApp)
* Support multiple persistence providers (memory, database, etc.)

---

## 3. Users

### 3.1 Tournament Organizer

* Creates tournaments
* Defines rules and formats
* Manages registrations
* Oversees scheduling

### 3.2 Player

* Registers to tournaments
* Defines availability
* Receives notifications

---

## 4. Core Features

### 4.1 User Management

* Register users
* Identify players and organizers

### 4.2 Tournament Management

* Create tournaments
* Define format:

  * Round-robin
  * Elimination
  * Group stage

### 4.3 Player Registration

* Register as individual or pair
* Assign players to teams/pairs

### 4.4 Availability Management

* Each pair defines available time slots
* System must consider availability in scheduling

### 4.5 Fixture Generation

* Generate zones/groups based on rules:

  * Seeded players
  * Balance criteria
* Generate matches accordingly

### 4.6 Scheduling

* Assign matches to time slots
* Avoid conflicts between players

### 4.7 Notifications (Future)

* Send updates via WhatsApp
* Confirm matches

---

## 5. Business Rules

* A player cannot be in two matches at the same time
* A pair must have at least one available time slot
* Tournament format determines fixture logic
* Seeding affects group distribution

---

## 6. Non-Functional Requirements

* Modular architecture
* Replaceable persistence layer
* Scalable to multiple tournaments
* API-first design

---

## 7. Future Scope

* AI-assisted fixture generation
* Integration with external APIs
* Ranking system
* Payment validation (for registrations)

---
