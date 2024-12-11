//
// examples/fetch_cxx11.cpp
// ------------------------
//
// Copyright (c) 2015 Daniel Joos
//
// Distributed under MIT license. (See file LICENSE)
//
// ----------------------------------
//
// This example shows how to create a 'FetchRequest' to get messages for a
// specific Topic & partition. On success, all received messages will be print
// to stdout.
// Your compiler needs to know about C++11 and respective flags need to be set!
//

#include <iostream>
#include <boost/asio.hpp>
#include <libkafka_asio/libkafka_asio.h>

using libkafka_asio::Client;
using libkafka_asio::FetchRequest;
using libkafka_asio::FetchResponse;
using libkafka_asio::MessageAndOffset;

int64_t current_offset = 0;
Client* client_ptr;
volatile size_t message_count{0};


time_t last_log_time;
time_t current_time;
time_t start_time;

void fetch_messages();

void handle_fetch_response(const boost::system::error_code& error, const FetchResponse::OptionalType& response) {
        if (error) {
            std::cerr << "Error fetching messages: " << error.message() << std::endl;
            return;
        }  

        // Process messages from the response
        // C++11 range-based for loop
        for (const auto &message : *response)
        {
            // Update offset to next message to fetch
            if (current_offset > message.offset() + 1)
            {
              current_offset = current_offset;
            }
            else
            {              
              current_offset = message.offset() + 1;
            }
			      message_count++;
        }

        // std::cout << message_count << std::endl;
        // Continue fetching messages
        fetch_messages();
    }


void fetch_messages() {
	
  // Create a 'Fetch' request and try to get data for partition 0 of topic
  // 'mytopic', starting with offset 1
  FetchRequest request;
  request.FetchTopic("my-topic-0000", 0, current_offset, 100000000 );

  time(&current_time);
  if (difftime(current_time, last_log_time) >= 1) {
			printf("elapsed:%f, messages: %.2fM, current_offset:%ul \n", difftime(current_time, start_time), (float)message_count / 1000000, current_offset);
			last_log_time = current_time;
	}

  // Send the prepared fetch request.
  // The client will attempt to automatically connect to the broker, specified
  // in the configuration.
  client_ptr->AsyncRequest(request, handle_fetch_response);
}

int main(int argc, char **argv)
{
  time(&last_log_time);
  time(&start_time);
  Client::Configuration configuration;
  configuration.auto_connect = true;
  configuration.client_id = "libkafka_asio_example2";
  configuration.socket_timeout = 10000;
  configuration.AddBrokerFromString("localhost:40002");
  configuration.message_max_bytes = 100000000;

  boost::asio::io_service ios;
  Client client(ios, configuration);
  client_ptr = &client;

  std::cout << " hello" << std::endl;

  fetch_messages();

  // Let's go!
  ios.run();
  return 0;
}
