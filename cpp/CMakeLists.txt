
add_executable(producer_cpp producer.cpp ${win32_sources})
target_link_libraries(producer_cpp PUBLIC rdkafka++)
